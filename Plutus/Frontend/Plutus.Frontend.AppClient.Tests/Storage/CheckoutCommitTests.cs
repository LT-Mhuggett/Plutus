using System;
using System.Collections.Generic;
using System.Linq;
using Database.Models;
using Plutus.Frontend.AppClient.Models;
using Plutus.Frontend.AppClient.Services.Storage;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Storage
{
    /// <summary>
    /// Cutover step 11 — turning the till's basket into sale lines and tenders.
    ///
    /// ⚠ THE POINT OF PUTTING THIS IN A SERVICE. `TillViewModel` is 1,200 lines with no test
    /// coverage whatsoever, and this is the money path. Everything that can be decided without a
    /// device lives here so it can be tested here; the viewmodel is left holding only the UI.
    /// </summary>
    public class CheckoutCommitTests
    {
        private static BasketItem Item(string idOne, decimal price, decimal exPrice, int qty = 1) =>
            new(new ItemModel
            {
                Id = idOne,
                Name = "Item " + idOne,
                Price = price,
                ExPrice = exPrice,
                Vat = new TaxModel { Name = "Standard" },
            }, qty);

        [Fact]
        public void Every_basket_item_becomes_a_line_carrying_its_barcode()
        {
            var lines = CheckoutCommit.LinesFrom(new IBasketRecord[]
            {
                Item("5010001", 14.99m, 12.49m),
                Item("5010002", 2.50m, 2.50m, qty: 3),
            });

            Assert.Equal(2, lines.Count);
            Assert.Equal("5010001", lines[0].IdOne);
            Assert.Equal(3, lines[1].Quantity);
        }

        /// <summary>
        /// ⚠ LOSSLESS ONLY BECAUSE OF STEP 10. These prices originated as PENCE from
        /// `EffectivePricePairAsync` and were divided by 100 purely to satisfy the legacy decimal
        /// model; multiplying back recovers exactly what was resolved. £14.99 → 1499, not 1498.
        /// </summary>
        [Fact]
        public void Decimal_prices_convert_back_to_the_pence_they_came_from()
        {
            var line = Assert.Single(CheckoutCommit.LinesFrom(new IBasketRecord[] { Item("A", 14.99m, 12.49m) }));

            Assert.Equal(1499, line.UnitIncPence);
            Assert.Equal(1249, line.UnitExPence);
        }

        /// <summary>
        /// ⚠ AND IT ROUNDS AWAY FROM ZERO, NOT TRUNCATES — which the test above does NOT prove.
        ///
        /// Found by mutation: replacing `Pence.FromDecimal` with `(long)(price * 100)` left every
        /// assertion above passing, because a 2dp decimal that came from pence scales exactly and
        /// the two are indistinguishable. They diverge the moment a third decimal place appears —
        /// which an apportioned discount or a legacy price can produce — and truncation loses a
        /// penny per line in the shop's favour, quietly, on the VAT return as well as the receipt.
        ///
        /// £0.125 → 13p away from zero, 12p truncated. Banker's rounding would also give 12p, which
        /// is why `Money.FromDecimal` states its mode rather than taking .NET's default.
        /// </summary>
        [Theory]
        [InlineData(0.125, 13)]
        [InlineData(0.135, 14)]
        [InlineData(0.005, 1)]
        public void A_fractional_penny_rounds_away_from_zero_rather_than_being_truncated(double price, long expected)
        {
            var line = Assert.Single(CheckoutCommit.LinesFrom(
                new IBasketRecord[] { Item("A", (decimal)price, (decimal)price) }));

            Assert.Equal(expected, line.UnitIncPence);
        }

        /// <summary>
        /// ⚠ The item id is left EMPTY on purpose. `SaleAssembler` derives it from the business id
        /// and the barcode and refuses a mismatched one, so exactly one place knows that rule —
        /// deriving it here as well would be a second implementation of the thing that decides
        /// which item a sale attaches to.
        /// </summary>
        [Fact]
        public void The_item_id_is_left_for_the_assembler_to_derive()
        {
            var line = Assert.Single(CheckoutCommit.LinesFrom(new IBasketRecord[] { Item("A", 1m, 1m) }));
            Assert.Equal(Guid.Empty, line.ItemId);
        }

        /// <summary>
        /// ⚠ Notes and alterations are NOT sale lines. An alteration's money is already reflected in
        /// the adjusted price of the line it applies to; emitting it as a line as well would take
        /// the discount off twice and under-charge the customer.
        /// </summary>
        [Fact]
        public void Notes_and_alterations_are_not_sale_lines()
        {
            var basket = new List<IBasketRecord>
            {
                Item("A", 10m, 8.33m),
                new BasketNote(new NoteModel { Note = "gift wrap" }),
            };

            var line = Assert.Single(CheckoutCommit.LinesFrom(basket));
            Assert.Equal("A", line.IdOne);
        }

        [Fact]
        public void An_empty_basket_produces_no_lines()
        {
            Assert.Empty(CheckoutCommit.LinesFrom(Array.Empty<IBasketRecord>()));
            Assert.Empty(CheckoutCommit.LinesFrom(null));
        }

        // ── tenders ──

        /// <summary>
        /// ⚠ THE ORDERING TRAP, reached through the till's own path. "Gift card" contains neither
        /// "cash" nor "online", so a naive chain files it as STORE CREDIT — a different liability
        /// with a different reconciliation, and the payment-split report groups by this byte.
        /// </summary>
        [Fact]
        public void Payment_method_names_map_to_the_shared_tender_bytes()
        {
            var tenders = CheckoutCommit.TendersFrom(new (string?, decimal, decimal)[]
            {
                ("Cash", 10.00m, 0.50m),
                ("Gift card", 5.00m, 0m),
                ("Store credit", 2.00m, 0m),
                ("Visa Debit", 1.00m, 0m),
            });

            Assert.Equal(Tenders.Cash, tenders[0].TenderType);
            Assert.Equal(Tenders.GiftCard, tenders[1].TenderType);
            Assert.Equal(Tenders.Credit, tenders[2].TenderType);
            Assert.Equal(Tenders.Card, tenders[3].TenderType);
        }

        [Fact]
        public void Tender_amounts_and_change_convert_to_pence()
        {
            var tender = Assert.Single(CheckoutCommit.TendersFrom(
                new (string?, decimal, decimal)[] { ("Cash", 20.00m, 5.01m) }));

            Assert.Equal(2000, tender.AmountPence);
            Assert.Equal(501, tender.ChangePence);
        }

        /// <summary>
        /// ⚠ NO TENDERS IS NOT "CASH". A basket that reached checkout with no payment row is a bug
        /// upstream, and defaulting it to cash here would record money as taken that nobody
        /// counted. The assembler refuses the sale instead.
        /// </summary>
        [Fact]
        public void No_payment_rows_produces_no_tenders_rather_than_assuming_cash()
        {
            Assert.Empty(CheckoutCommit.TendersFrom(Array.Empty<(string?, decimal, decimal)>()));
            Assert.Empty(CheckoutCommit.TendersFrom(null));
        }
    }
}
