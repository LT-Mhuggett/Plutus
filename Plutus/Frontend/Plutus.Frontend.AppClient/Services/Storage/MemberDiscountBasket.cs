using System;
using System.Collections.Generic;
using System.Linq;
using Database.Models;
using Plutus.Frontend.AppClient.Models;
using Plutus.SharedKernel;

namespace Plutus.Frontend.AppClient.Services.Storage
{
    /// <summary>
    /// The members' automatic discount, in the shape a MAUI basket can carry it — retrofit step 27.
    ///
    /// ⚠⚠ THIS CLOSES A LIVE MONEY DIFFERENCE. The web till applies the tier discount at
    /// `TillPage.tsx:122–123`; `autoDiscountRate` appeared NOWHERE in this app, so **a Gold member
    /// was charged 10% more on the MAUI till than on the web till for the same basket**. The rule was
    /// shared first (`SharedKernel.MemberDiscount`) precisely so this class could not invent a third
    /// answer — it decides nothing about eligibility or arithmetic, it only puts the shared verdict
    /// into a `BasketAlteration`.
    ///
    /// ⚠⚠ THE ASSOCIATION IS THE WHOLE DESIGN, AND IT IS NOT OPTIONAL. The web till stores a discount
    /// as a field ON each line, so its exclusions are per line for free. MAUI stores one
    /// `BasketAlteration` that `CheckoutCommit.ApplyAlterations` apportions at commit — and
    /// `TargetsOf` excludes **returns** but NOT already-discounted lines and NOT gift-card lines,
    /// both of which `MemberDiscount.LineIsEligible` excludes. So a whole-basket alteration would
    /// land the member's money on lines the shared rule says it must not touch, and the two tills
    /// would disagree about the same basket while both "used the shared rule".
    ///
    /// **The alteration therefore names its eligible items explicitly**, and `TargetsOf` then honours
    /// that list.
    ///
    /// ⚠ It is also what keeps the member discount clear of `BasketDiscounts`' ceiling: an alteration
    /// associated with three lines is judged against those three lines' worth.
    /// </summary>
    public static class MemberDiscountBasket
    {
        /// <summary>
        /// Which basket lines the members' discount may touch.
        ///
        /// ⚠ FOUR EXCLUSIONS, and only the first three come from the shared rule:
        ///   • a **return** — a refund gives back what was actually paid;
        ///   • an **already-discounted** line — NO STACKING, the manual or catalogue discount wins;
        ///   • a **gift card** — stored value is a liability, not a supply;
        ///   • ⚠ the **card surcharge**, which is MAUI's alone: the web till builds its fee inside
        ///     `checkout()` where no discount can reach it, while MAUI adds a real basket line for it
        ///     mid-tender. Without this a member would get 10% off the card fee on one till and not
        ///     the other — the same class of drift this whole class exists to remove.
        /// </summary>
        /// <param name="excluding">An alteration to ignore when deciding "already discounted" —
        /// pass the member's own alteration when RE-computing, or every line looks ineligible the
        /// second time and the discount silently collapses to nothing.</param>
        public static IReadOnlyList<BasketItem> EligibleLines(
            IEnumerable<IBasketRecord> basket, BasketAlteration excluding = null)
        {
            var records = (basket ?? Enumerable.Empty<IBasketRecord>()).ToList();

            var discounted = records
                .OfType<BasketAlteration>()
                .Where(a => !ReferenceEquals(a, excluding))
                .SelectMany(a => a.ItemsAssocitated ?? Enumerable.Empty<BasketItem>())
                .ToList();

            // ⚠ A whole-basket alteration (no association) discounts EVERYTHING, so every line is
            // already discounted and none is eligible. Answering "all of them" here would stack the
            // member's rate on top of an operator's "£5 off the basket".
            var everythingIsDiscounted = records
                .OfType<BasketAlteration>()
                .Any(a => !ReferenceEquals(a, excluding) &&
                          (a.ItemsAssocitated is null || !a.ItemsAssocitated.Any()));

            if (everythingIsDiscounted) return Array.Empty<BasketItem>();

            return records
                .OfType<BasketItem>()
                .Where(i => !i.IsReturn)
                .Where(i => !GiftCards.IsActivation(i.Item?.Id))
                .Where(i => !string.Equals(i.Item?.Id, CardSurchargeVat.ItemIdOne, StringComparison.OrdinalIgnoreCase))
                // ⚠ Reference identity, matching `CheckoutCommit.TargetsOf`'s first test. Two lines
                // of the same item are distinct baskets lines and only the discounted one is out.
                .Where(i => !discounted.Any(d => ReferenceEquals(d, i)))
                .ToList();
        }

        /// <summary>
        /// Build the members' alteration for this basket, or null when there is nothing to apply.
        ///
        /// ⚠ NULL IS THE NORMAL ANSWER in four cases and none of them is an error: no membership, an
        /// expired one, a zero rate, or a basket with no eligible line (all returns, all already
        /// discounted). The caller removes any previous member alteration either way.
        ///
        /// ⚠ THE ARITHMETIC IS THE SHARED RULE'S, per line, then summed — never the rate applied to a
        /// basket total. `LineDiscounts.Percentage` rounds on the whole line and caps at the line's
        /// value; a single multiplication over the basket would round once and disagree with the web
        /// till by a penny on mixed baskets, which is exactly the drift C2 exists to stop.
        /// </summary>
        /// <param name="operatorUserId">Who was signed in — for the audit record (default 22(c)).</param>
        public static BasketAlteration Build(
            IEnumerable<IBasketRecord> basket,
            string tierName,
            decimal autoDiscountRate,
            bool hasMembership,
            bool expired,
            Guid operatorUserId,
            BasketAlteration replacing = null)
        {
            if (!MemberDiscount.Applies(hasMembership, expired, autoDiscountRate)) return null;

            var eligible = EligibleLines(basket, excluding: replacing);
            if (eligible.Count == 0) return null;

            long incPence = 0, exPence = 0;

            foreach (var line in eligible)
            {
                // ⚠ Through `MemberDiscount.ForLine`, not `LineDiscounts.Percentage` directly, even
                // though eligibility has already been decided: it re-applies both gates, so a caller
                // that got `EligibleLines` wrong still cannot charge the wrong money. Defence in
                // depth on a money rule, the same shape as `LineDiscounts` re-guarding returns.
                incPence += MemberDiscount.ForLine(
                    line.PricePence, line.Quantity, autoDiscountRate,
                    hasMembership, expired,
                    isReturn: false, hasDiscount: false, isGiftCard: false);

                // ⚠ The ex half comes from the SAME rate on the ex price, mirroring how the manual
                // percentage path builds its pair. It is a display figure — `VatLineMath.ForLine`
                // re-splits the real money per line at commit — but a pair that disagrees with
                // itself would show an operator a VAT total that the receipt then contradicts.
                exPence += LineDiscounts.Percentage(
                    line.PriceExTaxPence, line.Quantity, autoDiscountRate, isReturn: false);
            }

            if (incPence <= 0) return null;

            var label = MemberDiscount.Label(tierName, autoDiscountRate);

            // ⚠ NEGATIVE, like every alteration — it is money coming off. `CheckoutCommit` works in
            // magnitudes and `BasketMoneyPence` sums signed prices, so the sign is load-bearing.
            var alteration = new BasketAlteration(
                new NoteModel { Note = label },
                // ⚠ The SENTINEL id, so this is distinguishable from an operator's own discount —
                // it is what lets the caller remove only the member's when a customer is detached,
                // and what keeps it out of the legacy bridge's `Transaction_Discount` rows.
                new DiscountModel { Id = MemberDiscount.SentinelDiscountId },
                eligible,
                -(incPence / 100m),
                -(exPence / 100m));

            // ⚠ Binding default 22(c) — "all discounts need to be tracked". An automatic discount
            // still gets a record: it is the one most likely to be queried later ("why is this
            // basket 10% under?"). ⚠ No authoriser, because nobody authorised it — the TIER did, and
            // the tier is portal-configured. Naming the operator would manufacture a self-approval.
            var authority = DiscountAudit.Automatic(label, incPence, operatorUserId);
            alteration.DiscountReason = authority.Reason;
            alteration.RequestedByUserId = authority.RequestedByUserId;
            alteration.AuthorisedByUserId = null;
            alteration.AuthorisedByName = null;

            return alteration;
        }

        /// <summary>
        /// The members' alteration currently on this basket, if any.
        ///
        /// ⚠ BY SENTINEL ID, never by label or by position. Detaching a customer must remove the
        /// member's discount and **leave the operator's own manual discounts alone** — matching the
        /// web till's `clearMemberDiscount`, which filters on `discountId === 0` for exactly this
        /// reason.
        /// </summary>
        public static BasketAlteration ExistingOn(IEnumerable<IBasketRecord> basket) =>
            (basket ?? Enumerable.Empty<IBasketRecord>())
                .OfType<BasketAlteration>()
                .FirstOrDefault(a => a.Discount != null &&
                                     a.Discount.Id == MemberDiscount.SentinelDiscountId);
    }
}
