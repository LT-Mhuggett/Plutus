using System;
using System.Collections.Generic;
using System.Linq;

namespace Plutus.SharedKernel;

/// <summary>
/// The <c>Discount.Type</c> codes, which are a wire/DB contract and not an enum anybody may renumber.
///
/// ⚠ These bytes live in the legacy <c>Discounts</c> table and in the web till's
/// <c>LineDiscount.type</c>, and every discount ever taken is stored against them. A renumber does
/// not break a build — it silently re-prices history.
/// </summary>
public static class DiscountKinds
{
    /// <summary>
    /// A fixed amount off EACH UNIT, carried as <see cref="ScheduledDiscount.FixedAmountPence"/>.
    ///
    /// ⚠⚠ INTEGER PENCE, and getting here took a correction. This rule originally carried one
    /// <c>decimal Amount</c> meaning POUNDS for this kind and a FRACTION for the other — which the
    /// architecture guard <c>No_module_declares_decimal_or_double_money_members</c> refused, and it
    /// was right to: a single field that is money in one branch and a ratio in the other is a unit
    /// error waiting for its first reader. The legacy <c>Discounts.Amount</c> column is still decimal
    /// pounds; the conversion happens ONCE, server-side, at the endpoint.
    /// </summary>
    public const int FixedAmount = 0;

    /// <summary>
    /// A percentage off the line, carried as <see cref="ScheduledDiscount.PercentFraction"/> — a
    /// FRACTION, so 0.10 is 10%.
    /// ⚠ Not a percent number. <see cref="LineDiscounts.Percentage"/> throws above 1.0 precisely
    /// because the legacy till multiplied by a typed "10" and charged ten times the price.
    /// </summary>
    public const int Percentage = 1;
}

/// <summary>
/// One scheduled discount rule as a till holds it: what it takes off, what it applies to, and when
/// it is live. "Wednesday Warhammer, 10% off the Warhammer category, on Wednesdays."
///
/// ⚠⚠ THE SCHEDULE TRAVELS RAW AND IS EVALUATED AGAINST THE TILL'S OWN CLOCK, and this is the whole
/// design — copied deliberately from <see cref="PermissionGrant"/>, whose header explains the same
/// trap in the same words. If the server answered "is this rule live now" at sync time, a till that
/// syncs on Monday and runs offline until Thursday would apply Monday's answer all week: no
/// Wednesday discount on Wednesday, or worse, a Wednesday discount every day. The fields have to
/// reach the till and the till has to ask at the moment of the sale.
///
/// ⚠ <see cref="WindowStartLocal"/>/<see cref="WindowEndLocal"/> are LOCAL wall-clock while
/// <see cref="ValidFromUtc"/>/<see cref="ValidToUtc"/> are UTC INSTANTS — the same deliberate split
/// as a permission grant, for the same reason: "Wednesdays 09:00–17:00" is a shop-floor fact about
/// local time, while "this promotion ends on the 31st" is an instant.
/// </summary>
/// <param name="Id">The catalogue discount's REAL id. ⚠ Load-bearing: it is what lets an
/// auto-applied rule ride <c>LineMeta.discounts[]</c> and project into <c>Transaction_Discount</c>,
/// which is keyed on a real <c>DiscountId</c>. A synthetic id would FK-fail every discounted sale —
/// which is exactly why the members' discount (sentinel 0) is kept OFF that array.</param>
/// <param name="DaysOfWeekMask">Bit 0 = Sunday … bit 6 = Saturday; null = any day. ⚠ Sunday = 0 to
/// match .NET's <see cref="DayOfWeek"/> and the permission twin — a mask built Monday-first silently
/// shifts every rule by a day.</param>
/// <param name="CategoryIds">Categories this rule targets. Empty + <paramref name="AllApplicable"/>
/// false means it targets NOTHING — see <see cref="IsWellFormed"/>.</param>
/// <param name="ItemIdOnes">Barcodes this rule targets, compared case-insensitively.</param>
/// <param name="PercentFraction">Used when <paramref name="Type"/> is
/// <see cref="DiscountKinds.Percentage"/>. A FRACTION — 0.10 is 10%. ⚠ A ratio, not money, which is
/// why it is legitimately a decimal.</param>
/// <param name="FixedAmountPence">Used when <paramref name="Type"/> is
/// <see cref="DiscountKinds.FixedAmount"/>. ⚠ INTEGER PENCE, per unit — money is integer pence
/// everywhere in this platform.</param>
public sealed record ScheduledDiscount(
    int Id,
    string Name,
    int Type,
    decimal PercentFraction = 0m,
    long FixedAmountPence = 0,
    bool AutoApply = false,
    bool AllApplicable = false,
    byte? DaysOfWeekMask = null,
    TimeOnly? WindowStartLocal = null,
    TimeOnly? WindowEndLocal = null,
    DateTime? ValidFromUtc = null,
    DateTime? ValidToUtc = null,
    IReadOnlyList<Guid>? CategoryIds = null,
    IReadOnlyList<string>? ItemIdOnes = null)
{
    /// <summary>
    /// Is this rule something a till may act on at all?
    ///
    /// ⚠⚠ IT FAILS TOWARDS NOT DISCOUNTING, AND THE TWO DIRECTIONS ARE NOT SYMMETRICAL. A rule that
    /// should have applied and did not charges the customer full price — visible at the counter, and
    /// the operator can still take it off by hand. A malformed rule that applies anyway gives money
    /// away silently, on every basket, until somebody audits the takings. So anything we cannot read
    /// with confidence is simply not a rule.
    ///
    /// ⚠⚠ THE EMPTY-TARGET CASE IS THE DANGEROUS ONE. <c>AllApplicable = false</c> with no categories
    /// and no items must mean **nothing**, never **everything** — the carrier-bag precedent
    /// (till-design C2: bags fail towards NONE, because a guessed price charges a customer money the
    /// shop never set). A join table that failed to load must not turn a category promotion into a
    /// whole-basket one.
    ///
    /// ⚠ A percentage outside 0–1 is refused here rather than left to throw inside
    /// <see cref="LineDiscounts.Percentage"/>: a portal typo must not put an exception on the selling
    /// path with a customer waiting. The writer refuses to save one; this is the reader's half.
    /// </summary>
    public bool IsWellFormed =>
        Type switch
        {
            // ⚠ A fraction ABOVE 1 is refused, not clamped: it is the shape of the legacy defect where
            // a typed "10" meant 1000%, and a rule that silently became "100% off" would be worse
            // than one that never applies.
            DiscountKinds.Percentage => PercentFraction > 0m && PercentFraction <= 1m,
            DiscountKinds.FixedAmount => FixedAmountPence > 0,
            _ => false,   // ⚠ An unrecognised kind is not a discount. Fails closed, like every gate here.
        }
        && (AllApplicable || (CategoryIds?.Count ?? 0) > 0 || (ItemIdOnes?.Count ?? 0) > 0);

    /// <summary>
    /// Is this rule live at the given moment?
    ///
    /// ⚠⚠ MIRRORS <see cref="PermissionGrant.IsActiveAt"/> EXPRESSION BY EXPRESSION, deliberately —
    /// including the parts that could be argued. Two "is it live now" rules in one codebase that
    /// disagree about a boundary are worse than one imperfect rule: a shop would find its 17:00
    /// discount ending at a different second from its 17:00 permission.
    ///
    /// ⚠ THE WINDOW IS INCLUSIVE AT BOTH ENDS (<c>t &lt; start</c> and <c>t &gt; end</c> are the
    /// refusals), so a 09:00–17:00 rule is live AT 17:00:00. That is the permission twin's behaviour
    /// and it is what this mirrors — not a half-open interval.
    ///
    /// ⚠ A WINDOW THAT WRAPS MIDNIGHT (22:00–02:00) MATCHES NOTHING, and that is copied on purpose
    /// too. The permission twin calls it "deliberately unhandled rather than half-handled"; fixing it
    /// here alone would make a late-night rule behave differently from a late-night permission, and
    /// the fix belongs in both at once.
    /// </summary>
    public bool IsLiveAt(DateTime nowLocal)
    {
        var nowUtc = nowLocal.Kind == DateTimeKind.Utc ? nowLocal : nowLocal.ToUniversalTime();
        if (ValidFromUtc.HasValue && nowUtc < ValidFromUtc.Value) return false;
        if (ValidToUtc.HasValue && nowUtc > ValidToUtc.Value) return false;

        if (DaysOfWeekMask.HasValue &&
            (DaysOfWeekMask.Value & (1 << (int)nowLocal.DayOfWeek)) == 0) return false;

        if (WindowStartLocal.HasValue || WindowEndLocal.HasValue)
        {
            var t = TimeOnly.FromDateTime(nowLocal);
            if (WindowStartLocal.HasValue && t < WindowStartLocal.Value) return false;
            if (WindowEndLocal.HasValue && t > WindowEndLocal.Value) return false;
        }
        return true;
    }

    /// <summary>
    /// Does this rule target this item — by "everything", by its category, or by its barcode?
    ///
    /// ⚠ The three targets are a UNION, not a precedence chain: a rule may name a category AND an
    /// extra item, and both land. There is no "most specific wins" here, because a rule that named
    /// one item would otherwise silently stop applying to its category.
    ///
    /// ⚠ Barcodes are compared ORDINAL-IGNORE-CASE. A barcode typed into the portal and a barcode
    /// read off a scanner are the same product in the shop's eyes, and a rule that missed because
    /// somebody typed a lower-case letter would be indistinguishable from a rule that was not live.
    /// </summary>
    public bool Targets(Guid? categoryId, string? itemIdOne)
    {
        if (AllApplicable) return true;
        if (categoryId.HasValue && CategoryIds is { Count: > 0 } cats && cats.Contains(categoryId.Value))
            return true;
        if (!string.IsNullOrWhiteSpace(itemIdOne) && ItemIdOnes is { Count: > 0 } items)
            return items.Any(i => string.Equals(i, itemIdOne, StringComparison.OrdinalIgnoreCase));
        return false;
    }
}

/// <summary>
/// Whether a scheduled discount applies to a basket line, and what it takes off.
///
/// ⚠⚠ THE ARITHMETIC IS NOT HERE, EXACTLY AS IN <see cref="MemberDiscount"/>. Pence come from
/// <see cref="LineDiscounts"/> and the net/VAT split from <see cref="VatLineMath.ForLine"/>. What
/// this class owns is the two questions a second till would otherwise have to guess: **is the rule
/// live**, and **may it land on this line**.
///
/// ⚠ ELIGIBILITY IS THE MEMBERS' DISCOUNT'S, NOT A SECOND OPINION — <see cref="MemberDiscount.LineIsEligible"/>
/// plus the card surcharge. A scheduled discount that discounted a return, stacked on a manual
/// discount, or knocked money off a gift card would be wrong for the identical reasons, and having
/// two eligibility rules is how the two drift.
/// </summary>
public static class ScheduledDiscounts
{
    /// <summary>
    /// Would this rule land on this line, right now? The whole question in one call, so no caller
    /// can check the schedule and forget the exclusions (or the reverse).
    /// </summary>
    /// <param name="isCardSurcharge">The line is the tenant's card fee. ⚠ A FEE IS NOT SHOPPING:
    /// discounting the surcharge means the shop passes on less than the acquirer charges it, so a
    /// promotion would quietly eat the cost the fee exists to recover.</param>
    public static bool LandsOn(
        ScheduledDiscount rule, DateTime nowLocal,
        Guid? categoryId, string? itemIdOne,
        bool isReturn, bool hasDiscount, bool isGiftCard, bool isCardSurcharge)
    {
        if (rule is null || !rule.IsWellFormed) return false;
        if (!rule.IsLiveAt(nowLocal)) return false;
        if (isCardSurcharge) return false;
        if (!MemberDiscount.LineIsEligible(isReturn, hasDiscount, isGiftCard)) return false;
        return rule.Targets(categoryId, itemIdOne);
    }

    /// <summary>
    /// The pence one rule takes off one line — 0 when it does not apply.
    ///
    /// ⚠ Both kinds go through <see cref="LineDiscounts"/> so a scheduled discount and a
    /// hand-applied one of the same size come to the same penny — and so a rule inherits the
    /// whole-line rounding and the cap at the line's value without restating either.
    ///
    /// ⚠ NO UNIT CONVERSION HAPPENS HERE ANY MORE. The pence arrive as pence and the fraction as a
    /// fraction; the one decimal-pounds figure in this feature lives in the legacy `Discounts.Amount`
    /// column and is converted once, server-side, where it is read.
    /// </summary>
    public static long ForLine(
        ScheduledDiscount rule, DateTime nowLocal,
        long unitIncPence, int quantity,
        Guid? categoryId, string? itemIdOne,
        bool isReturn, bool hasDiscount, bool isGiftCard, bool isCardSurcharge)
    {
        if (!LandsOn(rule, nowLocal, categoryId, itemIdOne, isReturn, hasDiscount, isGiftCard, isCardSurcharge))
            return 0;

        return rule.Type == DiscountKinds.FixedAmount
            ? LineDiscounts.FixedPerUnit(rule.FixedAmountPence, quantity, isReturn)
            : LineDiscounts.Percentage(unitIncPence, quantity, rule.PercentFraction, isReturn);
    }

    /// <summary>
    /// The AUTO-APPLY rules that would land on this line, biggest first — the candidates a till
    /// chooses from. Rules with <c>AutoApply = false</c> are catalogue entries an operator picks by
    /// hand and are never returned here.
    ///
    /// ⚠ ORDERED BIGGEST-PENCE-FIRST, THEN BY LOWEST ID, and the tie-break is not cosmetic: two
    /// rules worth the same money must resolve identically on both tills or the same basket totals
    /// differently on two counters. Id is the only stable key available offline.
    /// </summary>
    public static IReadOnlyList<(ScheduledDiscount Rule, long Pence)> CandidatesFor(
        IEnumerable<ScheduledDiscount>? rules, DateTime nowLocal,
        long unitIncPence, int quantity,
        Guid? categoryId, string? itemIdOne,
        bool isReturn, bool hasDiscount, bool isGiftCard, bool isCardSurcharge)
    {
        if (rules is null) return Array.Empty<(ScheduledDiscount, long)>();

        return rules
            .Where(r => r is not null && r.AutoApply)
            .Select(r => (Rule: r, Pence: ForLine(
                r, nowLocal, unitIncPence, quantity, categoryId, itemIdOne,
                isReturn, hasDiscount, isGiftCard, isCardSurcharge)))
            .Where(c => c.Pence > 0)
            .OrderByDescending(c => c.Pence)
            .ThenBy(c => c.Rule.Id)
            .ToList();
    }

    /// <summary>
    /// How a scheduled discount is LABELLED on the line and the receipt.
    ///
    /// ⚠ THE SHOP'S OWN NAME, and nothing appended. <see cref="MemberDiscount.Label"/> adds the rate
    /// because "Gold" alone does not say what it is worth — but a rule's name is free text the shop
    /// typed, and a shop that called its rule "Wednesday Warhammer 10%" would get the rate twice.
    /// The money off is already printed beside the name on both tills' line badges and on the
    /// receipt, so the figure is never missing.
    ///
    /// ⚠ A blank name still has to print SOMETHING, and it has to be the same something on every
    /// till — an empty receipt line reads as a fault, and two tills disagreeing about the fallback is
    /// the drift this exists to stop.
    /// </summary>
    public static string Label(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Discount" : name.Trim();
}
