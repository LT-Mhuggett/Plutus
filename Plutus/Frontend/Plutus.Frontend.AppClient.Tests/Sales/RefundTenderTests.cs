using System;
using System.Linq;
using Plutus.Frontend.AppClient.Services.Sales;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Sales
{
    /// <summary>
    /// A refund goes back the way it was paid.
    ///
    /// ⚠ Matt, 2026-08-11: *"Refunds need to ONLY offer the method that was used to pay. E.g. if it
    /// was a card payment, needs to go back to card."*
    ///
    /// ⚠ IT IS NOT A TIDINESS RULE. Refunding a card sale in cash is the oldest till fraud there is
    /// — buy on a card, return for notes, and the card is never debited in the end. The honest
    /// version does the same damage by accident: a day of card sales refunded in cash leaves the
    /// drawer short and the card takings untouched, and the Z read is the first thing that notices.
    ///
    /// ⚠ BUT IT IS A GUARD RAIL, NOT A LOCK. Every test below that offers MORE than the original
    /// tender exists because the alternative is an operator stuck in a checkout with nothing to
    /// press — which is the exact trap `TillTenders` was written to fix in the first place.
    /// </summary>
    public class RefundTenderTests
    {
        private static string[] Names(System.Collections.Generic.IReadOnlyList<TillTender> t) =>
            t.Select(x => x.Name).OrderBy(x => x).ToArray();

        [Fact]
        public void A_card_sale_refunds_only_to_CARD()
        {
            var offered = TillTenders.OfferedForRefund(new[] { Tenders.Card });

            Assert.Equal(new[] { "Card" }, Names(offered));
        }

        [Fact]
        public void A_cash_sale_refunds_only_to_CASH()
        {
            var offered = TillTenders.OfferedForRefund(new[] { Tenders.Cash });

            Assert.Equal(new[] { "Cash" }, Names(offered));
        }

        [Fact]
        public void A_SPLIT_sale_offers_both_ways_it_was_paid()
        {
            // ⚠ Somebody who paid £5 cash and £5 card must be able to be given both back. Picking
            // one and forcing the whole refund through it puts money in the wrong place.
            var offered = TillTenders.OfferedForRefund(new[] { Tenders.Cash, Tenders.Card });

            Assert.Equal(new[] { "Card", "Cash" }, Names(offered));
        }

        [Fact]
        public void An_UNKNOWN_origin_offers_everything()
        {
            // ⚠ Null means this till does not hold the original sale — a cross-till refund, or one
            // older than local history. Refusing there would block a legitimate refund at the
            // counter over a fact the till simply does not have.
            Assert.Equal(Names(TillTenders.Offered(refundOnly: true)),
                         Names(TillTenders.OfferedForRefund(null)));
        }

        [Fact]
        public void An_EMPTY_origin_list_offers_everything()
        {
            // Same reasoning as null — a sale whose tenders could not be read is not a sale that
            // was paid with nothing.
            Assert.Equal(Names(TillTenders.Offered(refundOnly: true)),
                         Names(TillTenders.OfferedForRefund(Array.Empty<byte>())));
        }

        [Fact]
        public void An_origin_paid_with_something_this_till_no_longer_OFFERS_falls_back()
        {
            // ⚠ A gift-card sale, say — `GiftCard` is deliberately not on the sheet (it draws down
            // a server-held balance and cannot be verified offline). Filtering strictly would leave
            // an EMPTY sheet and an operator trapped in a checkout with only Cancel to press.
            var offered = TillTenders.OfferedForRefund(new[] { Tenders.GiftCard });

            Assert.NotEmpty(offered);
            Assert.Equal(Names(TillTenders.Offered(refundOnly: true)), Names(offered));
        }

        [Fact]
        public void A_refund_never_gives_CHANGE_or_CASHBACK_however_it_is_restricted()
        {
            // ⚠ The rule `Offered(refundOnly: true)` already carried, and restricting the list must
            // not lose it: a refund hands money back, so change or cashback on top would hand over
            // the same money twice.
            foreach (var tender in TillTenders.OfferedForRefund(new[] { Tenders.Cash, Tenders.Card }))
            {
                Assert.False(tender.GivesChange, $"{tender.Name} must not give change on a refund");
                Assert.False(tender.GivesCashback, $"{tender.Name} must not give cashback on a refund");
            }
        }

        [Fact]
        public void The_names_still_map_back_to_their_wire_bytes()
        {
            // ⚠ The wire byte is derived from the NAME at commit (`Tenders.FromMethodName`), and a
            // name that maps elsewhere files the refund against the wrong liability. Restricting
            // the list must not disturb that.
            foreach (var tender in TillTenders.OfferedForRefund(new[] { Tenders.Cash, Tenders.Card }))
                Assert.Equal(tender.TenderType, Tenders.FromMethodName(tender.Name));
        }
    }
}
