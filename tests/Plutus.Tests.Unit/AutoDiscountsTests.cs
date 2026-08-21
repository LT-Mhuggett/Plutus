using System;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// The ONE automatic-discount resolver (C1/C2) — tier vs scheduled rule, decision D2.
///
/// ⚠⚠ THE CASE THIS CLASS EXISTS FOR is the last test in this file: a Gold member buying Warhammer on
/// a Wednesday must be charged the same on both tills. Without a shared resolver each till would
/// decide for itself what happens when two automatic discounts apply, and the two answers would
/// differ by real money on a basket nothing downstream can flag.
///
/// ⚠ Shared vectors with `till/autoDiscounts.test.ts` — add a case to one, add it to the other.
/// </summary>
public class AutoDiscountsTests
{
    private static readonly DateTime WedAfternoon = new(2026, 8, 19, 14, 0, 0, DateTimeKind.Local);
    private static readonly DateTime ThuAfternoon = new(2026, 8, 20, 14, 0, 0, DateTimeKind.Local);
    private const byte Wednesdays = 1 << 3;
    private static readonly Guid Warhammer = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Paint = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>A £5.00 Warhammer line, quantity 1 — the basket every case below rings up.</summary>
    private static AutoDiscountLine Line(
        bool isReturn = false, bool hasManualDiscount = false,
        bool isGiftCard = false, bool isCardSurcharge = false, Guid? category = null) =>
        new(UnitIncPence: 500, Quantity: 1, CategoryId: category ?? Warhammer,
            ItemIdOne: "SPACEMARINE-01",
            IsReturn: isReturn, HasManualDiscount: hasManualDiscount,
            IsGiftCard: isGiftCard, IsCardSurcharge: isCardSurcharge);

    private static ScheduledDiscount Rule(decimal fraction = 0.10m, int id = 7) => new(
        Id: id, Name: "Wednesday Warhammer", Type: DiscountKinds.Percentage, PercentFraction: fraction,
        AutoApply: true, DaysOfWeekMask: Wednesdays, CategoryIds: new[] { Warhammer });

    private static MemberStanding Gold(decimal rate = 0.10m) => new(true, false, rate, "Gold");

    // ── nothing applies ─────────────────────────────────────────────────────────

    [Fact]
    public void An_anonymous_sale_with_no_live_rule_takes_nothing()
    {
        Assert.Null(AutoDiscounts.ForLine(Line(), MemberStanding.None, new[] { Rule() }, ThuAfternoon));
    }

    [Fact]
    public void A_line_outside_the_rules_categories_takes_nothing()
    {
        Assert.Null(AutoDiscounts.ForLine(
            Line(category: Paint), MemberStanding.None, new[] { Rule() }, WedAfternoon));
    }

    // ── one source at a time ────────────────────────────────────────────────────

    [Fact]
    public void A_live_rule_alone_applies_and_carries_the_rules_REAL_id()
    {
        var got = AutoDiscounts.ForLine(Line(), MemberStanding.None, new[] { Rule() }, WedAfternoon);

        Assert.NotNull(got);
        Assert.Equal(50, got!.Pence);
        Assert.Equal(7, got.DiscountId);
        Assert.Equal("Wednesday Warhammer", got.Name);
        Assert.False(got.IsMemberDiscount);
    }

    /// <summary>⚠ The tier discount keeps the SENTINEL id (0). That is what holds it off
    /// <c>LineMeta.discounts[]</c>, whose legacy-bridge projection is keyed on a real
    /// <c>DiscountId</c> and would FK-fail on a synthetic one.</summary>
    [Fact]
    public void A_member_alone_applies_and_carries_the_SENTINEL_id()
    {
        var got = AutoDiscounts.ForLine(Line(), Gold(), null, WedAfternoon);

        Assert.NotNull(got);
        Assert.Equal(50, got!.Pence);
        Assert.Equal(MemberDiscount.SentinelDiscountId, got.DiscountId);
        Assert.Equal("Gold 10%", got.Name);
        Assert.True(got.IsMemberDiscount);
    }

    [Fact]
    public void An_expired_membership_grants_nothing()
    {
        var expired = new MemberStanding(true, true, 0.10m, "Gold");
        Assert.Null(AutoDiscounts.ForLine(Line(), expired, null, ThuAfternoon));
    }

    // ── D2: both apply ──────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ DECISION D2, AND THE REASON THIS CLASS EXISTS. A Gold member (10%) buying Warhammer on a
    /// Wednesday when the rule is 20%: the line takes 20%, ONCE. Never both — the tills are
    /// structurally one-discount-per-line — and never the smaller of the two promises the shop made.
    /// </summary>
    [Fact]
    public void When_both_apply_the_LARGER_wins_and_only_one_lands()
    {
        var got = AutoDiscounts.ForLine(Line(), Gold(0.10m), new[] { Rule(0.20m) }, WedAfternoon);

        Assert.NotNull(got);
        Assert.Equal(100, got!.Pence);                  // 20% of £5, not 30%
        Assert.Equal(7, got.DiscountId);                // the rule won
        Assert.Equal("Wednesday Warhammer", got.Name);
    }

    [Fact]
    public void When_the_member_is_worth_more_the_member_wins()
    {
        var got = AutoDiscounts.ForLine(Line(), Gold(0.20m), new[] { Rule(0.10m) }, WedAfternoon);

        Assert.NotNull(got);
        Assert.Equal(100, got!.Pence);
        Assert.True(got.IsMemberDiscount);
        Assert.Equal("Gold 20%", got.Name);
    }

    /// <summary>⚠ EQUAL MONEY KEEPS THE MEMBER'S BADGE. The choice is free, so it is fixed rather
    /// than left to iteration order — the member discount is the one the customer is told about at
    /// the counter and the one whose absence they would query.</summary>
    [Fact]
    public void A_tie_goes_to_the_member()
    {
        var got = AutoDiscounts.ForLine(Line(), Gold(0.10m), new[] { Rule(0.10m) }, WedAfternoon);

        Assert.NotNull(got);
        Assert.Equal(50, got!.Pence);
        Assert.True(got.IsMemberDiscount);
    }

    [Fact]
    public void The_biggest_of_several_live_rules_wins()
    {
        var got = AutoDiscounts.ForLine(
            Line(), MemberStanding.None,
            new[] { Rule(0.05m, id: 3), Rule(0.15m, id: 4), Rule(0.10m, id: 5) },
            WedAfternoon);

        Assert.NotNull(got);
        Assert.Equal(75, got!.Pence);
        Assert.Equal(4, got.DiscountId);
    }

    // ── manual always wins, and the resolver must not eat its own output ────────

    /// <summary>⚠ MANUAL BEATS AUTOMATIC. The operator's discount occupies the slot and the resolver
    /// steps back — <c>MemberDiscount.LineIsEligible</c>'s no-stacking rule, unchanged.</summary>
    [Fact]
    public void An_operators_own_discount_stops_both_sources()
    {
        Assert.Null(AutoDiscounts.ForLine(
            Line(hasManualDiscount: true), Gold(), new[] { Rule() }, WedAfternoon));
    }

    /// <summary>
    /// ⚠⚠ THE RE-ENTRANCY TRAP, PINNED. The resolver runs again on every basket change, and
    /// <c>HasManualDiscount</c> means the OPERATOR's discounts only. If a caller passed "this line
    /// has any discount at all" — including the one the resolver just wrote — the second scan would
    /// find every line ineligible and the discount would silently disappear from the basket.
    /// This test states the contract the callers have to honour.
    /// </summary>
    [Fact]
    public void The_resolver_still_answers_for_a_line_it_has_already_discounted()
    {
        var first = AutoDiscounts.ForLine(Line(), MemberStanding.None, new[] { Rule() }, WedAfternoon);
        Assert.NotNull(first);

        // Same line, re-resolved with HasManualDiscount still false — because nothing an OPERATOR
        // did has changed. The answer must be identical, not null.
        var again = AutoDiscounts.ForLine(Line(), MemberStanding.None, new[] { Rule() }, WedAfternoon);
        Assert.Equal(first, again);
    }

    // ── the exclusions, settled before either source is asked ───────────────────

    [Fact]
    public void A_return_takes_no_automatic_discount_from_either_source()
    {
        Assert.Null(AutoDiscounts.ForLine(Line(isReturn: true), Gold(), new[] { Rule() }, WedAfternoon));
    }

    [Fact]
    public void A_gift_card_takes_no_automatic_discount_from_either_source()
    {
        Assert.Null(AutoDiscounts.ForLine(Line(isGiftCard: true), Gold(), new[] { Rule() }, WedAfternoon));
    }

    /// <summary>⚠ A fee is not shopping — and this is the exclusion the shared
    /// <c>MemberDiscount.ForLine</c> does NOT carry, so it has to be settled here or each till would
    /// carry it separately (which is how the web till and MAUI came to hold two copies).</summary>
    [Fact]
    public void The_card_surcharge_takes_no_automatic_discount_from_either_source()
    {
        Assert.Null(AutoDiscounts.ForLine(Line(isCardSurcharge: true), Gold(), new[] { Rule() }, WedAfternoon));
    }
}
