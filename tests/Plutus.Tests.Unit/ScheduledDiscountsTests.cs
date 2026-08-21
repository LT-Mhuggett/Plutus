using System;
using System.Collections.Generic;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// Scheduled discounts — "Wednesday Warhammer" (C1/C2). ⚠⚠ THESE ARE THE SHARED VECTORS: the same
/// cases run in `till/scheduledDiscounts.test.ts`, and the register says **add a case to one, add it
/// to the other**. A twin pinned on one side only is not pinned — the card-surcharge row proved that
/// copying vectors across is not the same as copying their protection.
/// </summary>
public class ScheduledDiscountsTests
{
    // Wednesday 2026-08-19 at 14:00 local. Every "is it live" case is judged against this.
    private static readonly DateTime WedAfternoon = new(2026, 8, 19, 14, 0, 0, DateTimeKind.Local);
    private static readonly DateTime ThuAfternoon = new(2026, 8, 20, 14, 0, 0, DateTimeKind.Local);

    private const byte Wednesdays = 1 << 3;   // bit 0 = Sunday, so Wednesday is bit 3
    private static readonly Guid Warhammer = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Paint = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ScheduledDiscount WednesdayWarhammer(decimal fraction = 0.10m) => new(
        Id: 7, Name: "Wednesday Warhammer", Type: DiscountKinds.Percentage, PercentFraction: fraction,
        AutoApply: true, DaysOfWeekMask: Wednesdays,
        CategoryIds: new[] { Warhammer });

    // ── the schedule ────────────────────────────────────────────────────────────

    [Fact]
    public void A_Wednesday_rule_is_live_on_a_Wednesday_and_not_on_a_Thursday()
    {
        var rule = WednesdayWarhammer();
        Assert.True(rule.IsLiveAt(WedAfternoon));
        Assert.False(rule.IsLiveAt(ThuAfternoon));
    }

    /// <summary>⚠ Bit 0 = Sunday, matching .NET's <c>DayOfWeek</c> and the permission twin. A mask
    /// built Monday-first shifts every rule in the shop by one day, silently.</summary>
    [Theory]
    [InlineData(1 << 0, 2026, 8, 23, true)]   // Sunday
    [InlineData(1 << 0, 2026, 8, 24, false)]  // Monday against a Sunday mask
    [InlineData(1 << 6, 2026, 8, 22, true)]   // Saturday
    [InlineData(1 << 1, 2026, 8, 24, true)]   // Monday
    public void The_day_mask_is_Sunday_first(byte mask, int y, int m, int d, bool expected)
    {
        var rule = WednesdayWarhammer() with { DaysOfWeekMask = mask };
        Assert.Equal(expected, rule.IsLiveAt(new DateTime(y, m, d, 12, 0, 0, DateTimeKind.Local)));
    }

    [Fact]
    public void No_day_mask_means_every_day()
    {
        var rule = WednesdayWarhammer() with { DaysOfWeekMask = null };
        Assert.True(rule.IsLiveAt(WedAfternoon));
        Assert.True(rule.IsLiveAt(ThuAfternoon));
    }

    /// <summary>⚠⚠ THE WINDOW IS INCLUSIVE AT BOTH ENDS, mirroring
    /// <c>PermissionGrant.IsActiveAt</c> — a 09:00–17:00 rule is live AT 17:00:00. Pinned because the
    /// plausible "fix" is a half-open interval, which would end the discount a second before the
    /// permission that governs it.</summary>
    [Theory]
    [InlineData(8, 59, false)]
    [InlineData(9, 0, true)]     // inclusive start
    [InlineData(17, 0, true)]    // ⚠ inclusive END
    [InlineData(17, 1, false)]
    public void The_time_window_is_local_and_inclusive_at_both_ends(int hour, int minute, bool expected)
    {
        var rule = WednesdayWarhammer() with
        {
            WindowStartLocal = new TimeOnly(9, 0),
            WindowEndLocal = new TimeOnly(17, 0),
        };
        var at = new DateTime(2026, 8, 19, hour, minute, 0, DateTimeKind.Local);
        Assert.Equal(expected, rule.IsLiveAt(at));
    }

    /// <summary>⚠⚠ A window that wraps midnight matches NOTHING, and that is copied from the
    /// permission twin deliberately rather than fixed here. Fixing it on one side would make a
    /// late-night discount behave differently from a late-night permission.</summary>
    [Theory]
    [InlineData(23, 0)]
    [InlineData(1, 0)]
    [InlineData(12, 0)]
    public void A_window_that_wraps_midnight_matches_nothing(int hour, int minute)
    {
        var rule = WednesdayWarhammer() with
        {
            DaysOfWeekMask = null,
            WindowStartLocal = new TimeOnly(22, 0),
            WindowEndLocal = new TimeOnly(2, 0),
        };
        Assert.False(rule.IsLiveAt(new DateTime(2026, 8, 19, hour, minute, 0, DateTimeKind.Local)));
    }

    [Fact]
    public void Validity_dates_bound_the_rule_in_UTC()
    {
        var rule = WednesdayWarhammer() with
        {
            DaysOfWeekMask = null,
            ValidFromUtc = new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc),
            ValidToUtc = new DateTime(2026, 8, 19, 23, 59, 59, DateTimeKind.Utc),
        };
        Assert.True(rule.IsLiveAt(WedAfternoon));
        Assert.False(rule.IsLiveAt(ThuAfternoon));
    }

    // ── targeting ───────────────────────────────────────────────────────────────

    [Fact]
    public void A_category_rule_targets_its_category_and_nothing_else()
    {
        var rule = WednesdayWarhammer();
        Assert.True(rule.Targets(Warhammer, "SPACEMARINE-01"));
        Assert.False(rule.Targets(Paint, "PAINT-01"));
    }

    [Fact]
    public void An_item_rule_targets_its_barcodes_case_insensitively()
    {
        var rule = WednesdayWarhammer() with { CategoryIds = null, ItemIdOnes = new[] { "SPACEMARINE-01" } };
        Assert.True(rule.Targets(null, "SPACEMARINE-01"));
        Assert.True(rule.Targets(null, "spacemarine-01"));
        Assert.False(rule.Targets(null, "PAINT-01"));
    }

    /// <summary>The three targets are a UNION — naming one extra item must not stop a rule applying
    /// to the category it also names.</summary>
    [Fact]
    public void A_rule_may_target_a_category_AND_an_extra_item()
    {
        var rule = WednesdayWarhammer() with { ItemIdOnes = new[] { "PAINT-01" } };
        Assert.True(rule.Targets(Warhammer, "SPACEMARINE-01"));
        Assert.True(rule.Targets(Paint, "PAINT-01"));
        Assert.False(rule.Targets(Paint, "BRUSH-01"));
    }

    [Fact]
    public void AllApplicable_targets_everything_including_an_uncategorised_item()
    {
        var rule = WednesdayWarhammer() with { AllApplicable = true, CategoryIds = null };
        Assert.True(rule.Targets(null, null));
        Assert.True(rule.Targets(Paint, "PAINT-01"));
    }

    // ── well-formedness: the direction this fails in ────────────────────────────

    /// <summary>
    /// ⚠⚠ THE MONEY CASE ON THIS LIST. A rule that targets nothing must mean NOTHING, never
    /// EVERYTHING — the carrier-bag precedent. A join table that failed to load would otherwise turn
    /// a category promotion into a whole-basket one, on every sale, silently.
    /// </summary>
    [Fact]
    public void A_rule_that_targets_nothing_lands_on_nothing()
    {
        var rule = WednesdayWarhammer() with { AllApplicable = false, CategoryIds = null, ItemIdOnes = null };
        Assert.False(rule.IsWellFormed);
        Assert.False(rule.Targets(Warhammer, "SPACEMARINE-01"));
        Assert.Equal(0, ForOneLine(rule, WedAfternoon));
    }

    /// <summary>⚠ A percentage above 1.0 is refused rather than left to throw inside
    /// <c>LineDiscounts.Percentage</c>: a portal typo must not put an exception on the selling path
    /// with a customer waiting.</summary>
    [Theory]
    [InlineData(1.5)]
    [InlineData(0)]
    [InlineData(-0.1)]
    public void A_percentage_outside_zero_to_one_is_not_a_rule(decimal fraction)
    {
        var rule = WednesdayWarhammer(fraction);
        Assert.False(rule.IsWellFormed);
        Assert.Equal(0, ForOneLine(rule, WedAfternoon));
    }

    /// <summary>A FIXED rule of £1.50 is perfectly legal — the 0–1 ceiling is a percentage rule, and
    /// the two amounts are now separate fields so the ceiling cannot leak across.</summary>
    [Fact]
    public void A_fixed_amount_above_one_pound_is_legal()
    {
        var rule = WednesdayWarhammer() with
        {
            Type = DiscountKinds.FixedAmount, PercentFraction = 0m, FixedAmountPence = 150,
        };
        Assert.True(rule.IsWellFormed);
    }

    /// <summary>⚠ An unrecognised KIND is not a discount. Fails closed — a later backend's third
    /// discount type must not be applied by a till that has no idea what it means.</summary>
    [Fact]
    public void An_unknown_discount_kind_is_not_a_rule()
    {
        Assert.False((WednesdayWarhammer() with { Type = 99 }).IsWellFormed);
    }

    /// <summary>⚠ The two amount fields cannot cover for each other: a percentage rule with only a
    /// pence figure set is incomplete, and applying it would take a fraction of nothing off.</summary>
    [Fact]
    public void A_kind_with_the_WRONG_amount_field_set_is_not_a_rule()
    {
        Assert.False((WednesdayWarhammer() with
        {
            Type = DiscountKinds.Percentage, PercentFraction = 0m, FixedAmountPence = 150,
        }).IsWellFormed);

        Assert.False((WednesdayWarhammer() with
        {
            Type = DiscountKinds.FixedAmount, PercentFraction = 0.10m, FixedAmountPence = 0,
        }).IsWellFormed);
    }

    // ── the money ───────────────────────────────────────────────────────────────

    /// <summary>10% off a £5.00 line = 50p, through the shared <c>LineDiscounts.Percentage</c>.</summary>
    [Fact]
    public void A_percentage_rule_takes_the_shared_arithmetic()
    {
        Assert.Equal(50, ForOneLine(WednesdayWarhammer(), WedAfternoon));
    }

    /// <summary>⚠ A fixed rule's amount is PER UNIT and multiplied by quantity: £1 off three of
    /// something is £3, not £1.</summary>
    [Fact]
    public void A_fixed_rule_takes_its_pence_off_every_unit()
    {
        var rule = WednesdayWarhammer() with
        {
            Type = DiscountKinds.FixedAmount, PercentFraction = 0m, FixedAmountPence = 100,
        };
        Assert.Equal(300, ScheduledDiscounts.ForLine(
            rule, WedAfternoon, unitIncPence: 500, quantity: 3,
            categoryId: Warhammer, itemIdOne: "SPACEMARINE-01",
            isReturn: false, hasDiscount: false, isGiftCard: false, isCardSurcharge: false));
    }

    [Fact]
    public void Nothing_comes_off_when_the_rule_is_not_live()
    {
        Assert.Equal(0, ForOneLine(WednesdayWarhammer(), ThuAfternoon));
    }

    // ── the four exclusions, which are the member discount's and not a second opinion ──

    [Fact]
    public void A_return_takes_no_scheduled_discount()
    {
        Assert.Equal(0, ForOneLine(WednesdayWarhammer(), WedAfternoon, isReturn: true));
    }

    [Fact]
    public void A_line_the_operator_already_discounted_takes_no_scheduled_discount()
    {
        Assert.Equal(0, ForOneLine(WednesdayWarhammer(), WedAfternoon, hasDiscount: true));
    }

    [Fact]
    public void A_gift_card_takes_no_scheduled_discount()
    {
        Assert.Equal(0, ForOneLine(WednesdayWarhammer(), WedAfternoon, isGiftCard: true));
    }

    /// <summary>⚠ A fee is not shopping: discounting the card surcharge makes the shop pass on less
    /// than the acquirer charges it.</summary>
    [Fact]
    public void The_card_surcharge_takes_no_scheduled_discount()
    {
        Assert.Equal(0, ForOneLine(WednesdayWarhammer(), WedAfternoon, isCardSurcharge: true));
    }

    // ── candidates: only auto-apply rules, biggest first, ties by id ────────────

    /// <summary>A catalogue entry an operator picks by hand (<c>AutoApply = false</c>) must never
    /// apply itself — that is the difference between a rule and a list item.</summary>
    [Fact]
    public void A_rule_that_is_not_AutoApply_is_never_a_candidate()
    {
        var manual = WednesdayWarhammer() with { AutoApply = false };
        Assert.Empty(Candidates(manual));
    }

    [Fact]
    public void Candidates_come_back_biggest_first()
    {
        var ten = WednesdayWarhammer();
        var twenty = WednesdayWarhammer(0.20m) with { Id = 9 };
        var got = Candidates(ten, twenty);
        Assert.Equal(new long[] { 100, 50 }, new[] { got[0].Pence, got[1].Pence });
        Assert.Equal(9, got[0].Rule.Id);
    }

    /// <summary>⚠ Two rules worth the same money resolve by LOWEST ID, so both tills put the same
    /// badge on the same line. Left to iteration order this would depend on JSON ordering.</summary>
    [Fact]
    public void Rules_worth_the_same_break_the_tie_on_the_lower_id()
    {
        var later = WednesdayWarhammer() with { Id = 9, Name = "Later" };
        var earlier = WednesdayWarhammer() with { Id = 4, Name = "Earlier" };
        Assert.Equal(4, Candidates(later, earlier)[0].Rule.Id);
    }

    [Fact]
    public void A_null_rule_set_yields_no_candidates()
    {
        Assert.Empty(ScheduledDiscounts.CandidatesFor(
            null, WedAfternoon, 500, 1, Warhammer, "SPACEMARINE-01", false, false, false, false));
    }

    // ── the label ───────────────────────────────────────────────────────────────

    /// <summary>⚠ THE SHOP'S OWN NAME, with no rate appended — a shop that called its rule
    /// "Wednesday Warhammer 10%" would otherwise get the rate twice, and the money off is already
    /// printed beside it on both tills.</summary>
    [Fact]
    public void The_label_is_the_rule_name_as_the_shop_wrote_it()
    {
        Assert.Equal("Wednesday Warhammer", ScheduledDiscounts.Label("Wednesday Warhammer"));
        Assert.Equal("Wednesday Warhammer", ScheduledDiscounts.Label("  Wednesday Warhammer  "));
    }

    /// <summary>A blank name still has to print the same something on every till — an empty receipt
    /// line reads as a fault.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_falls_back_to_one_shared_word(string? name)
    {
        Assert.Equal("Discount", ScheduledDiscounts.Label(name));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private static long ForOneLine(
        ScheduledDiscount rule, DateTime at,
        bool isReturn = false, bool hasDiscount = false,
        bool isGiftCard = false, bool isCardSurcharge = false) =>
        ScheduledDiscounts.ForLine(
            rule, at, unitIncPence: 500, quantity: 1,
            categoryId: Warhammer, itemIdOne: "SPACEMARINE-01",
            isReturn, hasDiscount, isGiftCard, isCardSurcharge);

    private static IReadOnlyList<(ScheduledDiscount Rule, long Pence)> Candidates(params ScheduledDiscount[] rules) =>
        ScheduledDiscounts.CandidatesFor(
            rules, WedAfternoon, unitIncPence: 500, quantity: 1,
            categoryId: Warhammer, itemIdOne: "SPACEMARINE-01",
            isReturn: false, hasDiscount: false, isGiftCard: false, isCardSurcharge: false);
}
