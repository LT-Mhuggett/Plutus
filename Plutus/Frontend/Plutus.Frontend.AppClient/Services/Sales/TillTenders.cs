using System.Collections.Generic;
using System.Linq;
using Plutus.SharedKernel;

namespace Plutus.Frontend.AppClient.Services.Sales
{
    /// <summary>One way of paying, as the checkout sheet needs it.</summary>
    /// <param name="Name">⚠ Must map back through <see cref="Tenders.FromMethodName"/> to
    /// <paramref name="TenderType"/> — the wire byte is derived from this string at commit, and a
    /// name that maps elsewhere files the money against the wrong liability.</param>
    /// <param name="GivesChange">Change comes out of the drawer, so only cash can give it.</param>
    /// <param name="GivesCashback">Cashback is a CARD thing — the customer adds to a card payment
    /// and takes the difference in notes. It is not something cash can do.</param>
    public sealed record TillTender(string Name, byte TenderType, bool GivesChange, bool GivesCashback);

    /// <summary>
    /// The ways this till can take money (cutover step 13b, binding default 13).
    ///
    /// ⚠ THIS FIXED THE BUG THAT STOPPED A NEW TILL SELLING AT ALL. The checkout used to read the
    /// legacy local `PaymentMethodModel` table, which is seeded ONLY by `Database.Init()` — the
    /// legacy first-run path. A portal-provisioned till never runs it and has no local database at
    /// all, so the payment sheet rendered ZERO buttons and the `paid != sale.Total` loop could
    /// never terminate: the operator was trapped in a checkout with nothing to press but Cancel.
    /// Every dev machine hid it, because they were migrated from legacy installs that had already
    /// seeded Card and Cash.
    ///
    /// ⚠ IT IS A FIXED SET, NOT A ROSTER (binding default 13, Matt 2026-08-09). The platform takes
    /// exactly the <see cref="Tenders"/> bytes, the web till exposes exactly these, and a
    /// configurable roster of five constants is machinery without a requirement. A sixth tender is
    /// an additive change — one constant, one button — not a redesign.
    /// </summary>
    public static class TillTenders
    {
        /// <summary>
        /// What the sheet offers. ⚠ `Online` is never here: it is how a WEBSTORE order ingests, not
        /// something an operator can press. `Credit` and `GiftCard` are online-only by design (they
        /// draw down a server-held balance and cannot be verified offline) and arrive with their
        /// own steps — until then, offering a button that must fail is worse than not offering it.
        /// </summary>
        public static IReadOnlyList<TillTender> Offered(bool refundOnly = false)
        {
            var tenders = new List<TillTender>
            {
                new("Cash", Tenders.Cash, GivesChange: true, GivesCashback: false),
                new("Card", Tenders.Card, GivesChange: false, GivesCashback: true),
            };

            // ⚠ A refund gives money BACK, so nothing here gives change or cashback on top of it —
            // both would hand over the same money twice.
            return refundOnly
                ? tenders.Select(t => t with { GivesChange = false, GivesCashback = false }).ToList()
                : tenders;
        }

        /// <summary>
        /// What a REFUND may go back on — restricted to how the original sale was actually paid.
        ///
        /// ⚠ Matt, 2026-08-11: *"Refunds need to ONLY offer the method that was used to pay. E.g.
        /// if it was a card payment, needs to go back to card."*
        ///
        /// ⚠ IT IS NOT A TIDINESS RULE. Refunding a card sale in cash is the oldest till fraud there
        /// is: buy on a card, return for notes, and the card is never debited in the end. It is also
        /// how an honest shop accidentally empties its drawer — a day of card sales refunded in cash
        /// leaves the drawer short and the card takings untouched, and the Z read is the first thing
        /// that notices.
        ///
        /// ⚠ AN UNKNOWN ORIGIN OFFERS EVERYTHING, DELIBERATELY. `null` means this till does not hold
        /// the original sale — a cross-till refund, or a sale older than local history. Refusing
        /// there would block a legitimate refund at the counter over a fact the till simply does not
        /// have, which is a worse outcome than the one this prevents. The restriction is a guard
        /// rail, not a lock.
        ///
        /// ⚠ SO IS AN ORIGIN PAID BY SOMETHING THIS TILL NO LONGER OFFERS — a gift card, say. An
        /// empty list would leave the operator in a checkout with nothing to press, which is exactly
        /// the trap `Offered` was written to fix in the first place.
        /// </summary>
        /// <param name="originalTenderTypes">The <see cref="Tenders"/> bytes on the origin sale, or
        /// null when this till cannot see it.</param>
        public static IReadOnlyList<TillTender> OfferedForRefund(IReadOnlyCollection<byte> originalTenderTypes)
        {
            var all = Offered(refundOnly: true);

            if (originalTenderTypes is null || originalTenderTypes.Count == 0) return all;

            var matching = all.Where(t => originalTenderTypes.Contains(t.TenderType)).ToList();

            // ⚠ Never hand back an empty sheet — see the header.
            return matching.Count > 0 ? matching : all;
        }
    }
}
