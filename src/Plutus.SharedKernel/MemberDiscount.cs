using System;

namespace Plutus.SharedKernel;

/// <summary>
/// The members' automatic discount: **which lines it touches, and whether it applies at all.**
///
/// ⚠⚠ THIS EXISTS BECAUSE THE TWO TILLS ALREADY DISAGREE ABOUT IT, TODAY. The web till applies the
/// tier discount at `till/TillPage.tsx:122–123`; `autoDiscountRate` appears **nowhere** in
/// `Plutus.Frontend.AppClient`, which has no customer attach at all — so **a Gold member is charged
/// 10% more on the MAUI till than on the web till for the same basket**. Found 2026-08-13. The fix
/// is MAUI's attach screen (retrofit step 27); this class is the half that must not be re-derived
/// when that screen is written, because a members' discount is money and CLAUDE.md's C2 rule says a
/// money rule gets ONE home.
///
/// ⚠ THE ARITHMETIC IS NOT HERE — it is already shared. <see cref="LineDiscounts.Percentage"/> turns
/// a rate into pence (rounded on the whole line, capped at the line's value, refusing a fraction
/// above 1), and <see cref="VatLineMath.ForLine"/> splits those pence across net and VAT. What was
/// missing, and what a second till would have had to guess, is **eligibility**: the four conditions
/// below and the no-stacking policy.
///
/// ⚠ MIRRORS `basket.ts` CASE `applyMemberDiscount` EXACTLY. Binding default 10 — when in doubt,
/// match the web till. If the two ever disagree, the web till wins and the discrepancy is noted.
/// </summary>
public static class MemberDiscount
{
    /// <summary>
    /// The sentinel discount id a members' discount carries, distinguishing it from a manual or
    /// catalogue one.
    ///
    /// ⚠ IT IS LOAD-BEARING, NOT COSMETIC. The web till marks these lines `discountId: 0` so
    /// checkout keeps them out of the legacy bridge metadata, and so `clearMemberDiscount` can
    /// remove **only** the automatic discount when a customer is detached — without it, detaching a
    /// member would strip the operator's own manual discounts off the same basket.
    /// </summary>
    public const int SentinelDiscountId = 0;

    /// <summary>
    /// Does this membership grant a discount right now?
    ///
    /// ⚠ AN EXPIRED MEMBERSHIP GRANTS NOTHING, and the rate is ignored rather than trusted — a
    /// lapsed Gold member is a Gold member who is not currently entitled, and the row keeps its rate
    /// so it can be renewed. Matches `TillPage.tsx:122` (`m && !m.expired && m.autoDiscountRate > 0`).
    /// </summary>
    public static bool Applies(bool hasMembership, bool expired, decimal autoDiscountRate) =>
        hasMembership && !expired && autoDiscountRate > 0m;

    /// <summary>
    /// Is this basket line eligible for the members' discount?
    ///
    /// Three exclusions, each for its own reason — and all three are in `basket.ts`:
    ///   • <paramref name="isReturn"/> — returns take no discount at all
    ///     (<see cref="LineDiscounts"/>): a refund gives back what the customer actually paid, and
    ///     discounting it would refund them less than they handed over.
    ///   • <paramref name="hasDiscount"/> — **NO STACKING.** A manual or catalogue discount already
    ///     on the line WINS. Stacking a members' rate on top of a staff discount is how a basket
    ///     leaves for less than cost without anybody choosing that.
    ///   • <paramref name="isGiftCard"/> — a gift-card line is a **liability, not a supply**
    ///     (`GiftCardSettings`, and the gift-card VAT decision). Selling £50 of stored value for £45
    ///     hands over £50 of spendable money for £45 — it discounts money itself, and the discount
    ///     is then spent again on discounted goods.
    /// </summary>
    public static bool LineIsEligible(bool isReturn, bool hasDiscount, bool isGiftCard) =>
        !isReturn && !hasDiscount && !isGiftCard;

    /// <summary>
    /// The pence coming off one eligible line — or 0 when the line or the membership is not
    /// eligible. The one call a till needs; it composes the two questions above with the shared
    /// arithmetic so no caller can apply the rate to a line that should not have it.
    /// </summary>
    /// <param name="unitIncPence">The line's unit price, inc-VAT, in pence.</param>
    /// <param name="quantity">Line quantity.</param>
    /// <param name="autoDiscountRate">The tier's rate as a FRACTION — 0.10 for 10%.</param>
    public static long ForLine(
        long unitIncPence, int quantity, decimal autoDiscountRate,
        bool hasMembership, bool expired,
        bool isReturn, bool hasDiscount, bool isGiftCard)
    {
        if (!Applies(hasMembership, expired, autoDiscountRate)) return 0;
        if (!LineIsEligible(isReturn, hasDiscount, isGiftCard)) return 0;
        return LineDiscounts.Percentage(unitIncPence, quantity, autoDiscountRate, isReturn);
    }

    /// <summary>
    /// How a members' discount is LABELLED on the line and the receipt — e.g. <c>"Gold 10%"</c>.
    ///
    /// ⚠ Shared because the customer reads it. `TillPage.tsx:123` builds
    /// <c>`${m.tier} ${(m.autoDiscountRate * 100).toFixed(0)}%`</c>, and a till that wrote
    /// "Gold member discount" instead would put a different word on the receipt for the same money.
    /// ⚠ The percentage is rounded to a whole number for display ONLY — never for the arithmetic,
    /// which uses the unrounded fraction.
    /// </summary>
    public static string Label(string tierName, decimal autoDiscountRate) =>
        $"{tierName} {Math.Round(autoDiscountRate * 100m, MidpointRounding.AwayFromZero):0}%";
}
