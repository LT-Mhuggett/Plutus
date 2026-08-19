using System.Collections.Generic;
using System.Linq;
using Plutus.Frontend.AppClient.Helpers.CustomViews;
using Plutus.Frontend.AppClient.Views.CustomViews;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Sales
{
    /// <summary>
    /// The one-screen checkout's MONEY MAPPING — §5c item 2, 2026-08-19.
    ///
    /// ⚠⚠ This covers the step between the screen and the sale: typed text in, `TenderPayment` rows
    /// out. The arithmetic itself lives in `Client.Core.TenderSettlement` and is pinned there against
    /// the web till's vectors (`TenderSettlementTests` ↔ `tendering.test.ts`); what these prove is that
    /// the mapping does not lose, invent or mis-sign any of it on the way to the wire.
    ///
    /// ⚠ `Settle` is deliberately separable from the view for exactly this reason. A checkout whose
    /// only test is "a person clicked it" is how all three of the 2026-08-10 tendering defects shipped.
    /// </summary>
    public class CheckoutScreenTests
    {
        private static readonly List<CheckoutAlert.Row> Rows = new()
        {
            new CheckoutAlert.Row { PayId = 1, Name = "Cash", IsChangeable = true },
            new CheckoutAlert.Row { PayId = 2, Name = "Card", IsChangeable = false },
        };

        private static CheckoutHelper.Result Settle(long totalPence, params (int Id, string Text)[] typed) =>
            CheckoutHelper.Settle(totalPence, typed.ToDictionary(t => t.Id, t => t.Text), Rows);

        [Fact]
        public void A_settled_sale_carries_one_payment_per_filled_row()
        {
            var result = Settle(1000, (1, "4.00"), (2, "6.00"));

            Assert.NotNull(result);
            Assert.Equal(2, result.Payments.Count);
            Assert.Equal(400, result.Payments[0].AmountPence);
            Assert.Equal("Cash", result.Payments[0].MethodName);
            Assert.Equal(600, result.Payments[1].AmountPence);
        }

        /// <summary>⚠ An untouched row is not a £0.00 tender, and a £0 payment on the wire is a lie.</summary>
        [Fact]
        public void An_untouched_row_becomes_no_payment_at_all()
        {
            var result = Settle(1000, (1, "10.00"), (2, ""));

            Assert.Single(result.Payments);
            Assert.Equal("Cash", result.Payments[0].MethodName);
        }

        /// <summary>
        /// ⚠⚠ THE PAYMENT CARRIES ITS CHANGE, and Σchange must equal the overpay to the penny — the
        /// ingest invariant is `Σ tender − Σ change == GrossPence` and a sale that breaks it is
        /// quarantined, which `OutboxPusher` treats as terminal.
        /// </summary>
        [Fact]
        public void Change_rides_on_the_payment_that_gives_it()
        {
            var result = Settle(330, (1, "20.00"));

            Assert.Single(result.Payments);
            Assert.Equal(2000, result.Payments[0].AmountPence);
            Assert.Equal(1670, result.Payments[0].ChangePence);
            Assert.Equal(1670, result.ChangePence);
        }

        /// <summary>⚠ A card cannot give change, so this is not a sale with change — it is refused.</summary>
        [Fact]
        public void An_overpay_that_cannot_be_given_back_does_not_settle() =>
            Assert.Null(Settle(330, (2, "20.00")));

        [Fact]
        public void A_short_payment_does_not_settle() =>
            Assert.Null(Settle(1000, (1, "4.00")));

        [Fact]
        public void An_unreadable_amount_does_not_settle() =>
            Assert.Null(Settle(1000, (1, "abc")));

        /// <summary>
        /// ⚠⚠ A REFUND IS NEGATIVE ON THE WIRE. The ingest invariant compares tenders against a
        /// NEGATIVE gross, so positives here quarantine the refund — and a quarantined refund never
        /// retries, so the customer's money never goes back.
        /// </summary>
        [Fact]
        public void A_refund_sends_negative_tenders()
        {
            var result = Settle(-1000, (1, "10.00"));

            Assert.Single(result.Payments);
            Assert.Equal(-1000, result.Payments[0].AmountPence);
            Assert.Equal(0, result.Payments[0].ChangePence);
        }

        /// <summary>⚠ Handing back more than is owed is an error, not change.</summary>
        [Fact]
        public void A_refund_that_hands_back_too_much_does_not_settle() =>
            Assert.Null(Settle(-1000, (1, "12.00")));

        /// <summary>
        /// ⚠⚠ **THE DOUBLE-TAKE IS IMPOSSIBLE HERE BY CONSTRUCTION**, which is the structural half of
        /// the money defect of 2026-08-19. The sequential loop asked for a method and an amount over
        /// and over, so a capped tender could be chosen twice and take its cap each time. One box per
        /// method means one amount per method: there is no second pass to take, and this test pins the
        /// shape rather than a guard.
        /// </summary>
        [Fact]
        public void One_method_can_only_be_tendered_once()
        {
            var result = Settle(1000, (1, "4.00"), (2, "6.00"));

            Assert.Equal(
                result.Payments.Select(p => p.MethodName).Distinct().Count(),
                result.Payments.Count);
        }

        /// <summary>
        /// ⚠ Ordered by pay-method id, so the payment list — and so the RECEIPT — reads the same on two
        /// tills settling the same basket. Entered here in the opposite order on purpose.
        /// </summary>
        [Fact]
        public void Payments_come_back_in_a_stable_order()
        {
            var result = Settle(1000, (2, "6.00"), (1, "4.00"));

            Assert.Equal(new[] { "Cash", "Card" }, result.Payments.Select(p => p.MethodName).ToArray());
        }

        /// <summary>⚠⚠ Backing out yields NULL payments, never an empty list — D4 rule 4.</summary>
        [Fact]
        public void Abandoned_is_distinguishable_from_tendering_nothing()
        {
            Assert.True(CheckoutHelper.Result.Abandoned.IsAbandoned);
            Assert.Null(CheckoutHelper.Result.Abandoned.Payments);

            // ⚠ And "they want the gift-card box" is a third state, not a cancel: the caller must
            // reopen the checkout afterwards rather than dropping the basket.
            Assert.False(CheckoutHelper.Result.GiftCard.IsAbandoned);
            Assert.True(CheckoutHelper.Result.GiftCard.WantsGiftCard);
        }
    }
}
