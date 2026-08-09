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
        /// ⚠ Online is NOT offered: it is how a webstore order ingests, not a button. Credit and
        /// gift cards are online-only — they draw down a server-held balance that cannot be
        /// verified offline — and arrive with their own steps. A button that must fail is worse
        /// than no button.
        /// </summary>
        [Fact]
        public void Webstore_and_the_unbuilt_online_only_tenders_are_not_offered()
        {
            var bytes = TillTenders.Offered().Select(t => t.TenderType).ToList();

            Assert.DoesNotContain(Tenders.Online, bytes);
            Assert.DoesNotContain(Tenders.Credit, bytes);
            Assert.DoesNotContain(Tenders.GiftCard, bytes);
        }
    }
}
