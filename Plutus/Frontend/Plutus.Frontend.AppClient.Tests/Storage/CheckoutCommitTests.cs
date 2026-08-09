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

        /// <summary>
        /// A discount as the till really builds it: a separate record holding a NEGATIVE price,
        /// with the items it applies to hanging off it. ⚠ `ExecuteAlterTransaction` never writes
        /// the discount back onto `BasketItem.Price` — that mistaken belief is what step 11 shipped.
        /// </summary>
        private static BasketAlteration Alteration(decimal price, BasketItem applsTo) =>
            applsTo is null
                ? new BasketAlteration(new NoteModel { Note = "Discount" }, new DiscountModel(),
                    Array.Empty<BasketItem>(), price, price)
                : new BasketAlteration(new NoteModel { Note = "Discount" }, new DiscountModel(),
                    applsTo, price, price);

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
        /// A note carrying no money is not a sale line.
        ///
        /// ⚠ THIS TEST'S COMMENT USED TO CLAIM that an alteration's money "is already reflected in
        /// the adjusted price of the line it applies to". That was FALSE, and it vouched for a
        /// defect: `ExecuteAlterTransaction` appends a SEPARATE `BasketAlteration` record and never
        /// touches `BasketItem.Price`. See `A_discounted_basket_reconciles_with_what_the_customer_pays`.
        /// </summary>
        [Fact]
        public void A_note_with_no_money_on_it_is_not_a_sale_line()
        {
            var basket = new List<IBasketRecord>
            {
                Item("A", 10m, 8.33m),
                new BasketNote(new NoteModel { Note = "gift wrap" }),
            };

            var line = Assert.Single(CheckoutCommit.LinesFrom(basket));
            Assert.Equal("A", line.IdOne);
        }

        // ── discounts: the step 11 defect ──

        /// <summary>
        /// ⚠ THE P0 THIS FIXES. A discount is a separate `BasketAlteration` record holding a
        /// NEGATIVE price; `ExecuteAlterTransaction` never writes it back onto the item. The till's
        /// own total (`sale.Total`) sums EVERY basket record, so the tenders settled against the
        /// DISCOUNTED figure — while `LinesFrom` dropped the alteration entirely and assembled
        /// `GrossPence` from the UNDISCOUNTED lines.
        ///
        /// The server's invariant is `Σ tender − Σ change == GrossPence`. It would have failed on
        /// every discounted sale, answering `202 Quarantined` — which `OutboxPusher` treats as
        /// TERMINAL and never retries. The sale would have looked successful at the counter and
        /// been destroyed hours later.
        /// </summary>
        [Fact]
        public void A_discounted_basket_reconciles_with_what_the_customer_pays()
        {
            var item = Item("A", 10m, 10m, qty: 2);      // £20 of goods
            var basket = new List<IBasketRecord>
            {
                item,
                Alteration(-5m, item),                    // £5 off
            };

            var lines = CheckoutCommit.LinesFrom(basket);
            var line = Assert.Single(lines);

            Assert.Equal(500, line.DiscountPence);

            // The line's gross is what the customer actually pays, and it equals the till's own
            // basket total — which is the figure the tenders settle against.
            Assert.Equal(1500, line.UnitIncPence * line.Quantity - line.DiscountPence);
            Assert.Equal(1500, CheckoutCommit.BasketMoneyPence(basket));
        }

        /// <summary>⚠ A whole-basket discount splits across the lines with NOTHING LOST. Three
        /// lines sharing £10 by proportion is 333.33p each; the missing penny fails the server's
        /// reconcile invariant and quarantines the sale.</summary>
        [Fact]
        public void A_basket_wide_discount_is_apportioned_without_losing_a_penny()
        {
            var basket = new List<IBasketRecord>
            {
                Item("A", 10m, 10m), Item("B", 10m, 10m), Item("C", 10m, 10m),
                Alteration(-10m, null),                   // £10 off the basket, no association
            };

            var lines = CheckoutCommit.LinesFrom(basket);

            Assert.Equal(1000, lines.Sum(l => l.DiscountPence));
            Assert.Equal(2000, lines.Sum(l => l.UnitIncPence * l.Quantity - l.DiscountPence));
            Assert.Equal(2000, CheckoutCommit.BasketMoneyPence(basket));
        }

        /// <summary>A discount attached to one item does not come off another.</summary>
        [Fact]
        public void A_discount_lands_only_on_the_item_it_was_applied_to()
        {
            var cheap = Item("A", 10m, 10m);
            var dear = Item("B", 50m, 50m);
            var basket = new List<IBasketRecord> { cheap, dear, Alteration(-2m, dear) };

            var lines = CheckoutCommit.LinesFrom(basket);

            Assert.Equal(0, lines.Single(l => l.IdOne == "A").DiscountPence);
            Assert.Equal(200, lines.Single(l => l.IdOne == "B").DiscountPence);
        }

        // ── the card surcharge ──

        /// <summary>
        /// ⚠ THE LINE THE LEGACY BASKETNOTE COULD NEVER BE. A surcharged basket must RECONCILE —
        /// the fee is a real line, so `GrossPence` carries it and the tenders settle against the
        /// same figure. And the fee's VAT FOLLOWS THE BASKET (Bookit/NEC): on 20% goods the fee
        /// carries 20%, never a hardcoded rate and never zero.
        /// </summary>
        [Fact]
        public void A_surcharged_basket_reconciles_and_the_fee_follows_the_goods()
        {
            var basket = new List<IBasketRecord> { Item("A", 12m, 10m) };   // £12/£10 — 20% goods

            // 1.69% + 20p on £12.00 = 20.28 → 20p percent half + 20p flat = 40p fee.
            var fee = CheckoutCommit.SurchargeItem(basket, surchargeBp: 169, surchargeFlatPence: 20);

            Assert.NotNull(fee);
            Assert.Equal(0.40m, fee.Price);
            Assert.Equal(0.33m, fee.PriceExTax);   // 40 × 1000/1200 = 33.3 → 33p: the basket's own mix

            basket.Add(fee);
            var lines = CheckoutCommit.LinesFrom(basket);

            Assert.Equal(2, lines.Count);
            Assert.Equal(1240, lines.Sum(l => l.UnitIncPence * l.Quantity - l.DiscountPence));
            Assert.Equal(1240, CheckoutCommit.BasketMoneyPence(basket));   // guard passes
        }

        /// <summary>⚠ THE ONE A HARDCODED RATE GETS WRONG: a fee on zero-rated goods carries NO
        /// VAT — the fee follows the goods, and 20% here is output tax HMRC says is not due.</summary>
        [Fact]
        public void A_fee_on_zero_rated_goods_carries_no_VAT()
        {
            var fee = CheckoutCommit.SurchargeItem(
                new List<IBasketRecord> { Item("BOOK", 10m, 10m) }, 0, 50);

            Assert.Equal(0.50m, fee.Price);
            Assert.Equal(0.50m, fee.PriceExTax);   // ex == inc → VAT 0
        }

        /// <summary>⚠ A refund attracts no fee — surcharging money you are giving BACK is
        /// indefensible at the counter and the shared rule refuses the arithmetic anyway.</summary>
        [Fact]
        public void A_refund_only_basket_attracts_no_fee()
        {
            var basket = new List<IBasketRecord> { new BasketReturnItem(new ItemModel
            {
                Id = "A", Name = "A", Price = 10m, ExPrice = 10m, Vat = new TaxModel { Name = "" },
            }, 1) };

            Assert.Null(CheckoutCommit.SurchargeItem(basket, 169, 20));
        }

        [Fact]
        public void No_setting_no_fee()
        {
            Assert.Null(CheckoutCommit.SurchargeItem(
                new List<IBasketRecord> { Item("A", 10m, 10m) }, 0, 0));
        }

        /// <summary>Applied ONCE — a split payment across two cards must not charge the flat fee
        /// twice, and `HasSurcharge` is what the till checks before adding.</summary>
        [Fact]
        public void The_fee_is_detectable_so_it_is_only_added_once()
        {
            var basket = new List<IBasketRecord> { Item("A", 12m, 10m) };
            Assert.False(CheckoutCommit.HasSurcharge(basket));

            basket.Add(CheckoutCommit.SurchargeItem(basket, 169, 20));
            Assert.True(CheckoutCommit.HasSurcharge(basket));
        }

        /// <summary>
        /// ⚠ `BasketMoneyPence` must agree with the till's `sale.Total`, which is
        /// `Σ Price × Quantity`, returns negated — including the alteration's negative price.
        /// If these two ever disagree the reconciliation guard fires on honest baskets and the
        /// till refuses sales it should take.
        /// </summary>
        [Fact]
        public void The_basket_total_matches_the_sum_the_till_settles_against()
        {
            var item = Item("A", 10m, 10m, qty: 2);
            var basket = new List<IBasketRecord> { item, Alteration(-5m, item) };

            var asTheTillSumsIt = basket.Sum(r => r.Price * (r is BasketReturnItem ? -1 : 1) * r.Quantity);

            Assert.Equal(15m, asTheTillSumsIt);
            Assert.Equal(1500, CheckoutCommit.BasketMoneyPence(basket));
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
