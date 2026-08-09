using System;
using System.Collections.Generic;
using Plutus.Contracts.Client;
using Plutus.Frontend.AppClient.Services.Printing;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Printing
{
    /// <summary>
    /// Cutover step 14 — the receipt states what was COMMITTED.
    ///
    /// ⚠ THE REGRESSION THIS CLOSES. The footer printed `SaleModel.Id`, and step 11 deleted the
    /// legacy `db.Save()` that assigned it — so every receipt since has carried an EMPTY barcode.
    /// Nothing threw. The barcode is the only way a customer's receipt finds its sale again for a
    /// reprint or a receipt-led refund, and step 15's read path keys on exactly this id.
    /// </summary>
    public class ReceiptSaleTests
    {
        private static IngestSaleRequest Request(long gross, long vat, params IngestTender[] tenders) =>
            new()
            {
                SaleId = Guid.Parse("01931f3c-0000-7000-8000-0000000000aa"),
                OccurredAtUtc = new DateTime(2026, 8, 9, 13, 30, 0, DateTimeKind.Utc),
                GrossPence = gross,
                VatPence = vat,
                Lines = new List<IngestLine>(),
                Tenders = new List<IngestTender>(tenders),
            };

        /// <summary>⚠ THE ONE THAT MATTERS. The receipt carries the PLATFORM sale id — the thing
        /// the platform can actually be asked about.</summary>
        [Fact]
        public void The_receipt_carries_the_platform_sale_id()
        {
            var receipt = ReceiptSale.From(Request(1200, 200), null);

            Assert.Equal(Guid.Parse("01931f3c-0000-7000-8000-0000000000aa"), receipt.SaleId);
            Assert.NotEqual(Guid.Empty, receipt.SaleId);
        }

        /// <summary>
        /// ⚠ Every money figure is the COMMITTED one, and ex is derived as gross − VAT rather than
        /// re-summed. Two independent totals for one sale means the customer's paper and the
        /// platform's record can differ by a penny with no way to tell which was charged.
        /// </summary>
        [Fact]
        public void The_money_is_the_committed_money()
        {
            var receipt = ReceiptSale.From(Request(1200, 200), null);

            Assert.Equal(1200, receipt.GrossPence);
            Assert.Equal(200, receipt.VatPence);
            Assert.Equal(1000, receipt.ExPence);
        }

        /// <summary>⚠ The customer's clock. A receipt timestamped an hour out is the sort of thing
        /// that gets queried at a chargeback, and the payload carries UTC.</summary>
        [Fact]
        public void The_time_printed_is_local_not_utc()
        {
            var receipt = ReceiptSale.From(Request(1200, 200), null);

            Assert.Equal(DateTimeKind.Local, receipt.WhenLocal.Kind);
            Assert.Equal(new DateTime(2026, 8, 9, 13, 30, 0, DateTimeKind.Utc).ToLocalTime(), receipt.WhenLocal);
        }

        /// <summary>The operator's own words for the tender — the payload carries a BYTE, and a
        /// receipt reading "1" where the customer expects "Card" is worse than useless.</summary>
        [Fact]
        public void Tender_names_come_from_the_till()
        {
            var receipt = ReceiptSale.From(
                Request(1200, 200, new IngestTender { TenderType = Tenders.Cash, AmountPence = 1400, ChangePence = 200 }),
                new[] { "Cash" });

            var tender = Assert.Single(receipt.Tenders);
            Assert.Equal("Cash", tender.Name);
            Assert.Equal(1400, tender.AmountPence);
            Assert.Equal(200, tender.ChangePence);
        }

        /// <summary>⚠ And a readable fallback when the till supplies no name — never the raw byte.</summary>
        [Fact]
        public void A_missing_name_falls_back_to_something_a_customer_can_read()
        {
            var receipt = ReceiptSale.From(
                Request(500, 0, new IngestTender { TenderType = Tenders.GiftCard, AmountPence = 500 }), null);

            Assert.Equal("Gift card", Assert.Single(receipt.Tenders).Name);
        }

        /// <summary>Change is totalled across every tender — a split payment gives change once.</summary>
        [Fact]
        public void Change_is_totalled_across_tenders()
        {
            var receipt = ReceiptSale.From(Request(1000, 0,
                new IngestTender { TenderType = Tenders.Card, AmountPence = 500 },
                new IngestTender { TenderType = Tenders.Cash, AmountPence = 700, ChangePence = 200 }), null);

            Assert.Equal(200, receipt.ChangePence);
        }

        /// <summary>⚠ Refusing a null payload rather than printing a blank receipt: a receipt with
        /// no sale behind it is a document that says money changed hands when nothing was recorded.</summary>
        [Fact]
        public void There_is_no_receipt_without_a_committed_sale()
        {
            Assert.Throws<ArgumentNullException>(() => ReceiptSale.From(null, null));
        }
    }
}
