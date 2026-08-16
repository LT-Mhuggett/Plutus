using System.Linq;
using Plutus.Frontend.AppClient.Services.Sales;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Sales
{
    /// <summary>
    /// Cutover step 13b — the ways a till can take money.
    ///
    /// ⚠ THE BUG THESE EXIST FOR: the checkout read a legacy table that a portal-provisioned till
    /// never seeds, so a brand-new till showed a payment sheet with NO BUTTONS and its
    /// `paid != sale.Total` loop could never terminate. Nothing failed, nothing logged — the
    /// operator was simply trapped mid-sale.
    /// </summary>
    public class TillTendersTests
    {
        /// <summary>⚠ THE ONE THAT MATTERS. A till with no local database must still be able to
        /// take money — that is the entire failure this step closes.</summary>
        [Fact]
        public void A_till_with_no_local_database_can_still_take_money()
        {
            Assert.NotEmpty(TillTenders.Offered());
        }

        /// <summary>
        /// ⚠ Every name must map back to the byte it claims. The wire byte is derived from this
        /// STRING at commit (`Tenders.FromMethodName`), so a name that maps elsewhere files real
        /// money against the wrong liability and every payment-split report inherits it.
        /// </summary>
        [Fact]
        public void Every_offered_name_maps_back_to_its_own_tender_byte()
        {
            foreach (var tender in TillTenders.Offered())
                Assert.Equal(tender.TenderType, Tenders.FromMethodName(tender.Name));
        }

        [Fact]
        public void Cash_and_card_are_both_offered()
        {
            var names = TillTenders.Offered().Select(t => t.Name).ToList();
            Assert.Contains("Cash", names);
            Assert.Contains("Card", names);
        }

        /// <summary>⚠ Change comes out of the DRAWER — only cash can give it. A card marked
        /// changeable would let the till hand over notes it never took.</summary>
        [Fact]
        public void Only_cash_gives_change()
        {
            var offered = TillTenders.Offered();

            Assert.True(offered.Single(t => t.TenderType == Tenders.Cash).GivesChange);
            Assert.False(offered.Single(t => t.TenderType == Tenders.Card).GivesChange);
        }

        /// <summary>⚠ Cashback is the mirror image and the pairing is easy to get backwards:
        /// cashback is a CARD transaction — the customer overpays by card and takes the difference
        /// in notes. Cash cannot do it.</summary>
        [Fact]
        public void Only_card_gives_cashback()
        {
            var offered = TillTenders.Offered();

            Assert.True(offered.Single(t => t.TenderType == Tenders.Card).GivesCashback);
            Assert.False(offered.Single(t => t.TenderType == Tenders.Cash).GivesCashback);
        }

        /// <summary>⚠ A refund hands money BACK, so nothing may give change or cashback on top —
        /// both would pay the same money out twice.</summary>
        [Fact]
        public void A_refund_gives_neither_change_nor_cashback()
        {
            Assert.All(TillTenders.Offered(refundOnly: true), t =>
            {
                Assert.False(t.GivesChange);
                Assert.False(t.GivesCashback);
            });
        }

        /// <summary>
        /// ⚠ Online is NOT offered: it is how a webstore order ingests, not a button. Gift cards
        /// arrive with WP13. ⚠ Store credit is offered ONLY when there is a balance to spend — see
        /// the tests below — so the default call still shows neither.
        /// </summary>
        [Fact]
        public void Webstore_and_the_unbuilt_tenders_are_not_offered()
        {
            var bytes = TillTenders.Offered().Select(t => t.TenderType).ToList();

            Assert.DoesNotContain(Tenders.Online, bytes);
            Assert.DoesNotContain(Tenders.GiftCard, bytes);
            Assert.DoesNotContain(Tenders.Credit, bytes);   // no balance passed = nothing to spend
        }

        // ── store credit (step 27) ────────────────────────────────────────────────────────────

        /// <summary>
        /// ⚠ THE BUTTON APPEARS ONLY WHEN IT CAN BE SPENT. Store credit draws down a server-held
        /// balance, so it cannot be verified or redeemed offline — and the caller passes 0 when
        /// nobody is attached, the balance is empty, or the till is offline. A button that can only
        /// fail is worse than no button: that is the exact trap `Offered` was written to fix when a
        /// portal-provisioned till rendered a payment sheet with nothing on it at all.
        /// </summary>
        [Theory]
        [InlineData(0L, false)]
        [InlineData(-100L, false)]
        [InlineData(1L, true)]
        [InlineData(2500L, true)]
        public void Store_credit_is_offered_only_when_there_is_a_balance(long balance, bool expected)
        {
            var offered = TillTenders.Offered(refundOnly: false, creditAvailablePence: balance)
                .Any(t => t.TenderType == Tenders.Credit);

            Assert.Equal(expected, offered);
        }

        /// <summary>
        /// ⚠⚠ NEVER ON A REFUND, and this is a money rule rather than a UI choice. Binding default
        /// 19: a refund goes back **only to the tender that took the money**. Store credit took
        /// none, so refunding INTO it would mint spendable value out of a return — the gift-card
        /// cash-out exploit wearing a different hat. The web till filters it out for the same reason
        /// (`CheckoutDialog.tsx:141`).
        /// </summary>
        [Fact]
        public void Store_credit_is_never_offered_on_a_refund_however_large_the_balance()
        {
            var offered = TillTenders.Offered(refundOnly: true, creditAvailablePence: 999_99)
                .Any(t => t.TenderType == Tenders.Credit);

            Assert.False(offered);
        }

        /// <summary>
        /// ⚠⚠ THE NAME IS THE WIRE BYTE. `Tenders.FromMethodName` matches on substrings and tests
        /// "gift" BEFORE "credit", so a name like "Gift credit" would file this money against the
        /// gift-card liability — a different balance, a different reconciliation, and nothing
        /// anywhere would flag it.
        /// </summary>
        [Fact]
        public void The_store_credit_button_maps_back_to_the_credit_tender_byte()
        {
            var credit = TillTenders.Offered(creditAvailablePence: 500)
                .Single(t => t.TenderType == Tenders.Credit);

            Assert.Equal(Tenders.Credit, Tenders.FromMethodName(credit.Name));
            Assert.DoesNotContain("gift", credit.Name, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>⚠ Store credit gives no change and no cashback. Handing notes back against a
        /// credit balance converts non-refundable stored value into cash from the drawer.</summary>
        [Fact]
        public void Store_credit_gives_neither_change_nor_cashback()
        {
            var credit = TillTenders.Offered(creditAvailablePence: 500)
                .Single(t => t.TenderType == Tenders.Credit);

            Assert.False(credit.GivesChange);
            Assert.False(credit.GivesCashback);
        }

        /// <summary>⚠ And it does not displace the ordinary tenders — a member paying partly on
        /// credit still needs cash or card for the rest.</summary>
        [Fact]
        public void Offering_store_credit_leaves_cash_and_card_in_place()
        {
            var bytes = TillTenders.Offered(creditAvailablePence: 500).Select(t => t.TenderType).ToList();

            Assert.Contains(Tenders.Cash, bytes);
            Assert.Contains(Tenders.Card, bytes);
            Assert.Contains(Tenders.Credit, bytes);
        }
    }
}
