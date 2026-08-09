using System;
using System.Collections.Generic;
using Plutus.Frontend.AppClient.Services.Sales;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Sales
{
    /// <summary>
    /// Cutover step 16 — deciding a return.
    ///
    /// ⚠ WHAT THIS REPLACED. The legacy block queried the local legacy `Trans` table (empty on a
    /// portal till, permanently) and called `trans.First()`, which throws from an `async void` with
    /// no catch the moment the item is not on that sale — so scanning the wrong receipt, an
    /// ordinary counter mistake, CLOSED THE APPLICATION. Its "refunds left" test counted quantities
    /// on that one machine, so goods bought on another till could be refunded here in full, twice.
    ///
    /// `ResolveAsync` needs a device and a server, so the tests exercise `Decide` — the part that
    /// turns a sale record into a money decision, which is the part that can be wrong about money.
    /// </summary>
    public class ReturnLookupTests
    {
        private static ReturnLookup.SaleLines Sale(params ReturnLookup.SaleLineFacts[] lines) =>
            new(GrossPence: SumOf(lines), Lines: lines);

        private static long SumOf(IEnumerable<ReturnLookup.SaleLineFacts> lines)
        {
            long total = 0;
            foreach (var l in lines) total += l.LineGrossPence;
            return total;
        }

        private static ReturnLookup.SaleLineFacts Line(string idOne, long incPence, long exPence, int qty = 1) =>
            new(idOne, qty, incPence, exPence, incPence * qty);

        // ── the crash this closes ──

        /// <summary>
        /// ⚠ THE ONE THAT USED TO CLOSE THE APP. `trans.First()` threw when the item was not on the
        /// sale. It is a sentence now, and the operator keeps their basket.
        /// </summary>
        [Fact]
        public void An_item_that_is_not_on_the_sale_is_refused_not_a_crash()
        {
            var sale = Sale(Line("5010001", 1200, 1000));

            var result = ReturnLookup.Decide(
                SaleRecordSource.Server, sale, itemIdOne: "9999999", quantity: 1,
                alreadyRefundedPence: 0, saleId: Guid.NewGuid());

            Assert.False(result.IsAllowed);
            Assert.Contains("isn't on this sale", result.Decision.Reason);
        }

        [Fact]
        public void A_sale_with_no_lines_at_all_is_refused()
        {
            var result = ReturnLookup.Decide(
                SaleRecordSource.Server, Sale(), "5010001", 1, 0, Guid.NewGuid());

            Assert.False(result.IsAllowed);
        }

        // ── the money ──

        /// <summary>⚠ THE PRICE THE CUSTOMER PAID, off the original sale — not today's catalogue
        /// price. A price that has moved since would refund the wrong amount, and the direction it
        /// goes wrong is whichever way the shop loses.</summary>
        [Fact]
        public void The_refund_uses_the_price_from_the_original_sale()
        {
            var sale = Sale(Line("5010001", 1499, 1249));

            var result = ReturnLookup.Decide(
                SaleRecordSource.Server, sale, "5010001", 1, 0, Guid.NewGuid());

            Assert.True(result.IsAllowed);
            Assert.Equal(1499, result.UnitIncPence);
            Assert.Equal(1249, result.UnitExPence);   // the VAT split the sale actually settled
        }

        [Fact]
        public void Quantity_multiplies_what_is_asked_for()
        {
            var sale = Sale(Line("5010001", 500, 500, qty: 5));

            var result = ReturnLookup.Decide(
                SaleRecordSource.Server, sale, "5010001", quantity: 3, alreadyRefundedPence: 0, saleId: Guid.NewGuid());

            Assert.True(result.IsAllowed);
            Assert.Equal(1500, result.Decision.AllowedPence);
        }

        /// <summary>
        /// ⚠ BINDING DEFAULT 12 — you cannot refund more than was paid. Asking for more than is
        /// left is CAPPED, and the caller is told so: silently handing over less than was asked for
        /// is how a refund becomes an argument at the counter.
        /// </summary>
        [Fact]
        public void Asking_for_more_than_is_left_is_capped_and_says_so()
        {
            var sale = Sale(Line("5010001", 3000, 2500));

            var result = ReturnLookup.Decide(
                SaleRecordSource.Server, sale, "5010001", quantity: 1,
                alreadyRefundedPence: 2000, saleId: Guid.NewGuid());

            Assert.True(result.IsAllowed);
            Assert.True(result.Decision.WasCapped);
            Assert.Equal(1000, result.Decision.AllowedPence);        // £30 − £20 already back
            Assert.Equal(2000, result.Decision.AlreadyRefundedPence);
        }

        /// <summary>A sale already refunded in full has nothing left, and says that rather than
        /// paying out zero silently.</summary>
        [Fact]
        public void A_sale_already_refunded_in_full_has_nothing_left()
        {
            var sale = Sale(Line("5010001", 3000, 2500));

            var result = ReturnLookup.Decide(
                SaleRecordSource.Server, sale, "5010001", 1, alreadyRefundedPence: 3000, saleId: Guid.NewGuid());

            Assert.False(result.IsAllowed);
            Assert.Equal(RefundVerdict.NothingLeft, result.Decision.Verdict);
        }

        // ── which record answered ──

        /// <summary>
        /// ⚠ OUTSIDE THE WINDOW, THIS TILL MUST NOT GUESS. Its own record cannot see a refund taken
        /// on another till, so past the rolling window the answer is "reconnect" — never a payout.
        /// </summary>
        [Fact]
        public void A_local_record_outside_the_window_refuses_and_asks_for_a_connection()
        {
            var sale = Sale(Line("5010001", 1200, 1000));

            var result = ReturnLookup.Decide(
                SaleRecordSource.LocalOutsideWindow, sale, "5010001", 1, 0, Guid.NewGuid());

            Assert.False(result.IsAllowed);
            Assert.Equal(RefundVerdict.NeedsConnection, result.Decision.Verdict);
        }

        [Fact]
        public void A_local_record_inside_the_window_is_good_enough()
        {
            var sale = Sale(Line("5010001", 1200, 1000));

            var result = ReturnLookup.Decide(
                SaleRecordSource.LocalInWindow, sale, "5010001", 1, 0, Guid.NewGuid());

            Assert.True(result.IsAllowed);
        }

        /// <summary>⚠ "Not ours" and "can't reach the server" are DIFFERENT messages. Telling an
        /// operator to check the connection when the receipt is simply not ours sends them to
        /// reboot a router for no reason.</summary>
        [Fact]
        public void An_unknown_sale_is_not_reported_as_a_connection_problem()
        {
            var result = ReturnLookup.Decide(
                SaleRecordSource.NotFound, Sale(Line("5010001", 1200, 1000)), "5010001", 1, 0, Guid.NewGuid());

            Assert.Equal(RefundVerdict.UnknownSale, result.Decision.Verdict);
            Assert.DoesNotContain("Reconnect", result.Decision.Reason);
        }

        // ── the sale id off a receipt ──

        /// <summary>
        /// ⚠ BOTH FORMS. The receipt barcode prints the id with no dashes, because that is what
        /// fits a barcode; an operator reading it off a portal screen types the dashed form.
        /// Refusing either is a legitimate receipt that cannot be refunded.
        /// </summary>
        [Theory]
        [InlineData("01931f3c000070008000000000000011")]
        [InlineData("01931f3c-0000-7000-8000-000000000011")]
        [InlineData("  01931f3c-0000-7000-8000-000000000011  ")]
        public void A_sale_id_is_accepted_dashed_or_not(string text)
        {
            Assert.True(ReturnLookup.TryParseSaleId(text, out var id));
            Assert.Equal(Guid.Parse("01931f3c-0000-7000-8000-000000000011"), id);
        }

        /// <summary>⚠ An empty GUID is not a sale — it is what a failed parse looks like, and
        /// treating it as one would look the "all zeroes" sale up and refuse it for the wrong
        /// reason.</summary>
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("not-a-guid")]
        [InlineData("00000000-0000-0000-0000-000000000000")]
        public void Rubbish_and_the_empty_guid_are_refused(string text)
        {
            Assert.False(ReturnLookup.TryParseSaleId(text, out _));
        }
    }
}
