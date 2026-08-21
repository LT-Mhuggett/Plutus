using System;
using System.Collections.Generic;

namespace Plutus.SharedKernel;

/// <summary>
/// The facts about one basket line that decide whether an automatic discount may land on it.
///
/// ⚠ A RECORD RATHER THAN EIGHT LOOSE ARGUMENTS ON PURPOSE. Four of these are booleans that mean
/// completely different things, and a positional mix-up between <see cref="IsReturn"/> and
/// <see cref="HasManualDiscount"/> would compile, pass a careless test, and quietly discount refunds.
/// </summary>
/// <param name="CategoryId">The line's category, as the till already holds it
/// (<c>CatalogueItemDto.CategoryId</c> / the web till's <c>item.catId</c>) — so targeting is answered
/// OFFLINE, at the scanner, with no round trip.</param>
/// <param name="HasManualDiscount">⚠⚠ **A DISCOUNT THE OPERATOR PUT THERE — never one this resolver
/// put there.** The resolver runs again on every basket change, so if its own output counted as
/// "already discounted" the second pass would find every line ineligible and the discount would
/// vanish the moment a second item was scanned. Callers must pass the operator's discounts only;
/// both tills mark automatic ones so they can tell the difference.</param>
public readonly record struct AutoDiscountLine(
    long UnitIncPence,
    int Quantity,
    Guid? CategoryId,
    string? ItemIdOne,
    bool IsReturn,
    bool HasManualDiscount,
    bool IsGiftCard,
    bool IsCardSurcharge);

/// <summary>
/// What the attached customer's membership entitles them to right now — or
/// <see cref="None"/> when no customer is on the sale.
/// </summary>
public readonly record struct MemberStanding(
    bool HasMembership,
    bool Expired,
    decimal AutoDiscountRate,
    string? TierName)
{
    /// <summary>No customer attached: an anonymous sale, which is most sales.</summary>
    public static MemberStanding None => new(false, false, 0m, null);
}

/// <summary>
/// The automatic discount a line takes — the tier's, or a scheduled rule's, whichever is worth more.
/// </summary>
/// <param name="DiscountId">The catalogue id to record. ⚠ <see cref="MemberDiscount.SentinelDiscountId"/>
/// (0) for a tier discount, which is what keeps it OFF <c>LineMeta.discounts[]</c> and out of the
/// legacy bridge's FK; a real id for a scheduled rule, which projects cleanly.</param>
/// <param name="Reason">Auto-filled, and mandatory by binding default 22(c). Nobody typed it because
/// nobody decided anything — the rule or the tier IS the reason.</param>
/// <param name="PercentFraction">The rate, when <paramref name="Type"/> is a percentage — a FRACTION,
/// so 0.10 is 10%. A ratio rather than money, which is why it is a decimal.</param>
/// <param name="FixedAmountPence">The per-unit amount, when <paramref name="Type"/> is fixed.
/// ⚠ INTEGER PENCE.</param>
/// <param name="Pence">What actually comes off THIS line, already computed by the shared arithmetic.
/// ⚠ Callers should charge this rather than re-deriving it from the two fields above — that is how a
/// second rounding creeps in.</param>
public sealed record AutoDiscount(
    int DiscountId,
    string Name,
    int Type,
    decimal PercentFraction,
    long FixedAmountPence,
    long Pence,
    string Reason)
{
    /// <summary>Is this the members' tier discount rather than a scheduled rule?</summary>
    public bool IsMemberDiscount => DiscountId == MemberDiscount.SentinelDiscountId;
}

/// <summary>
/// ONE resolver for every automatic discount, on every till.
///
/// ⚠⚠ THIS CLASS EXISTS BECAUSE THE ALTERNATIVE IS TWO ENGINES RACING. Before scheduled discounts
/// there was exactly one automatic discount — the member's tier — and each till applied it its own
/// way (the web till onto the line, MAUI as a basket alteration). Adding a second source without a
/// single resolver would mean each till deciding for itself what happens when both apply, and
/// "customer got 10% on one till and 15% on the other for the same basket on the same Wednesday" is
/// a money defect nothing downstream can detect.
///
/// ⚠⚠ DECISION D2 — THE LINE TAKES THE LARGER, NEVER BOTH. Both tills are structurally
/// one-discount-per-line, so stacking is not merely disallowed, it is inexpressible. Largest-wins is
/// the customer-best reading of two promises the shop has made, and it is deterministic, which
/// "whichever was applied first" is not.
///
/// ⚠ MANUAL ALWAYS BEATS AUTOMATIC. An operator's own discount occupies the slot and this resolver
/// skips the line entirely — that is <see cref="MemberDiscount.LineIsEligible"/>'s no-stacking rule,
/// unchanged, and it is why <see cref="AutoDiscountLine.HasManualDiscount"/> means the operator's
/// discounts only.
/// </summary>
public static class AutoDiscounts
{
    /// <summary>
    /// The automatic discount this line should carry right now, or null for none.
    ///
    /// ⚠ THE TIE GOES TO THE MEMBER. Equal money either way, so the choice is free — and the member
    /// discount is the one that was already there before scheduled discounts existed, the one the
    /// customer is told about at the counter, and the one whose absence they would query. Fixed
    /// rather than left to iteration order, because two tills must land on the same badge.
    /// </summary>
    public static AutoDiscount? ForLine(
        AutoDiscountLine line,
        MemberStanding member,
        IEnumerable<ScheduledDiscount>? rules,
        DateTime nowLocal)
    {
        // ⚠ A fee is not shopping, and a return refunds what was actually paid. Both are settled
        // before anything else so neither source can reach them by a different route.
        if (line.IsCardSurcharge || line.IsReturn) return null;
        if (line.HasManualDiscount) return null;

        var memberPence = MemberDiscount.ForLine(
            line.UnitIncPence, line.Quantity, member.AutoDiscountRate,
            member.HasMembership, member.Expired,
            line.IsReturn, line.HasManualDiscount, line.IsGiftCard);

        var candidates = ScheduledDiscounts.CandidatesFor(
            rules, nowLocal, line.UnitIncPence, line.Quantity,
            line.CategoryId, line.ItemIdOne,
            line.IsReturn, line.HasManualDiscount, line.IsGiftCard, line.IsCardSurcharge);

        var best = candidates.Count > 0 ? candidates[0] : default;

        // Nothing on either side.
        if (memberPence <= 0 && best.Pence <= 0) return null;

        // ⚠ `>=` is the tie rule stated above: equal money keeps the member's badge.
        if (memberPence >= best.Pence)
        {
            var label = MemberDiscount.Label(member.TierName ?? string.Empty, member.AutoDiscountRate);
            return new AutoDiscount(
                MemberDiscount.SentinelDiscountId, label,
                DiscountKinds.Percentage, member.AutoDiscountRate, FixedAmountPence: 0,
                memberPence, label);
        }

        var name = ScheduledDiscounts.Label(best.Rule.Name);
        return new AutoDiscount(
            best.Rule.Id, name, best.Rule.Type,
            best.Rule.PercentFraction, best.Rule.FixedAmountPence,
            best.Pence, name);
    }
}
