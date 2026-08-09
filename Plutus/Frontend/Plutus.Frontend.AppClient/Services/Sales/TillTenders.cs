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
    }
}
