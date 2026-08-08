using System;
using Plutus.Reporting;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP2c — restating past VAT returns (Matt's instruction, 2026-08-08: "correct past return").
///
/// Two things have to be right for a restatement to be worth filing: WHICH RETURN a day belongs to
/// (get the stagger wrong and every correction lands on the wrong period), and WHICH CORRECTION
/// ROUTE the net error takes (HMRC Notice 700/45 §4).
/// </summary>
public class VatCorrectionTests
{
    // ── HMRC Notice 700/45 §4: threshold = greater of £10,000 and 1% of Box 6, capped at £50,000 ──

    [Fact]
    public void Threshold_floor_is_ten_thousand_pounds_for_a_small_business()
    {
        // 1% of £200,000 is £2,000 — below the floor, so the floor applies.
        Assert.Equal(10_000_00, VatGuidance.ErrorCorrectionThresholdPence(200_000_00));
        // A business with no turnover at all still gets the floor, not zero.
        Assert.Equal(10_000_00, VatGuidance.ErrorCorrectionThresholdPence(0));
    }

    [Fact]
    public void Threshold_rises_with_turnover_once_one_percent_beats_the_floor()
    {
        // 1% of £2,000,000 = £20,000, which is above the £10,000 floor.
        Assert.Equal(20_000_00, VatGuidance.ErrorCorrectionThresholdPence(2_000_000_00));
    }

    [Fact]
    public void Threshold_is_capped_at_fifty_thousand_however_large_the_turnover()
    {
        // 1% of £100m would be £1m; the cap holds it at £50,000.
        Assert.Equal(50_000_00, VatGuidance.ErrorCorrectionThresholdPence(100_000_000_00));
    }

    [Fact]
    public void Kapow_sized_error_is_adjusted_on_the_next_return_not_disclosed_on_a_VAT652()
    {
        // The real case: £10.77 against a floor threshold of £10,000. Nowhere near.
        Assert.Equal("adjust-next-return", VatGuidance.ErrorCorrectionRoute(10_77, 500_000_00));
    }

    [Fact]
    public void An_error_over_the_threshold_needs_a_VAT652()
    {
        Assert.Equal("vat652", VatGuidance.ErrorCorrectionRoute(10_000_01, 200_000_00));
        // …and one exactly ON the threshold is still within it (Notice 700/45 says "below or equal").
        Assert.Equal("adjust-next-return", VatGuidance.ErrorCorrectionRoute(10_000_00, 200_000_00));
    }

    [Fact]
    public void The_route_test_is_on_the_ABSOLUTE_error_so_an_over_declaration_is_treated_alike()
    {
        // An over-declaration is still an error to correct — it just moves the other way (Box 4).
        Assert.Equal("vat652", VatGuidance.ErrorCorrectionRoute(-25_000_00, 200_000_00));
        Assert.Equal("adjust-next-return", VatGuidance.ErrorCorrectionRoute(-500_00, 200_000_00));
    }

    // ── VAT period allocation ──────────────────────────────────────────────────────────────────
    // ⚠ Quarters follow the business's HMRC STAGGER GROUP, not calendar quarters. A business on
    // stagger 2 files Nov–Jan, Feb–Apr, … — attributing its corrections to Jan–Mar would put every
    // one of them on the wrong return.

    [Theory]
    // Stagger 1 — quarters END Mar/Jun/Sep/Dec, so they run Jan–Mar, Apr–Jun, …
    [InlineData(3, "2026-01-15", "2026-01-01", "2026-03-31")]
    [InlineData(3, "2026-03-31", "2026-01-01", "2026-03-31")]
    [InlineData(3, "2026-04-01", "2026-04-01", "2026-06-30")]
    [InlineData(3, "2026-12-31", "2026-10-01", "2026-12-31")]
    // Stagger 2 — quarters END Jan/Apr/Jul/Oct, so they run Nov–Jan, Feb–Apr, …
    [InlineData(1, "2026-01-15", "2025-11-01", "2026-01-31")]
    [InlineData(1, "2026-02-01", "2026-02-01", "2026-04-30")]
    [InlineData(1, "2025-12-25", "2025-11-01", "2026-01-31")]
    // Stagger 3 — quarters END Feb/May/Aug/Nov, so they run Dec–Feb, Mar–May, …
    [InlineData(2, "2026-01-15", "2025-12-01", "2026-02-28")]
    [InlineData(2, "2026-08-08", "2026-06-01", "2026-08-31")]
    public void A_day_lands_in_the_quarter_its_stagger_group_actually_files(
        int staggerEndMonth, string day, string expectStart, string expectEnd)
    {
        var (_, start, end) = ReportsController.VatPeriodOf(
            DateOnly.Parse(day), "quarter", staggerEndMonth);
        Assert.Equal(DateOnly.Parse(expectStart), start);
        Assert.Equal(DateOnly.Parse(expectEnd), end);
    }

    [Fact]
    public void Every_quarter_is_exactly_three_months_and_never_leaves_a_gap()
    {
        // The property that matters: consecutive days can only ever be in the same quarter or in
        // adjacent ones. A gap or an overlap would double-count or lose a day's takings.
        foreach (var stagger in new[] { 1, 2, 3 })
        {
            var day = new DateOnly(2024, 1, 1);
            var (_, _, prevEnd) = ReportsController.VatPeriodOf(day, "quarter", stagger);
            while (day < new DateOnly(2027, 1, 1))
            {
                day = day.AddDays(1);
                var (_, start, end) = ReportsController.VatPeriodOf(day, "quarter", stagger);
                if (end != prevEnd)
                {
                    // A new quarter must start the very day after the last one ended.
                    Assert.Equal(prevEnd.AddDays(1), start);
                    Assert.Equal(start.AddMonths(3).AddDays(-1), end);
                    prevEnd = end;
                }
                Assert.InRange(day, start, end);
            }
        }
    }

    [Fact]
    public void Monthly_periods_are_calendar_months()
    {
        var (key, start, end) = ReportsController.VatPeriodOf(new DateOnly(2026, 2, 14), "month", 3);
        Assert.Equal("2026-02", key);
        Assert.Equal(new DateOnly(2026, 2, 1), start);
        Assert.Equal(new DateOnly(2026, 2, 28), end);
    }

    [Fact]
    public void The_restatement_is_the_gap_between_summed_lines_and_the_VAT_fraction()
    {
        // The defect, in miniature. Three £14.99 lines at 20%:
        //   as filed: each line's VAT is 1499 − 1249 = 250, summed = 750
        //   restated: the fraction on £44.97 = 749.5 → 750 … so pick takings where they DIVERGE.
        // £2.99 × 3: line VAT 299 − 249 = 50 each → 150 filed.
        //            fraction on 897 = 149.5 → 150. Still equal.
        // The divergence is real but small per basket; it accumulates over thousands. Use a case
        // that shows it plainly: takings of 1000p at 20% is 166.67 → 167, while a till that sold
        // ten 100p lines charges 10 × (100 − 83) = 170.
        const long takings = 1000;
        Assert.Equal(167, VatAccounting.OutputTaxOn(takings, 2000));

        long summed = 0;
        for (var i = 0; i < 10; i++)
        {
            var ex = (long)Math.Round(100 / 1.2m, MidpointRounding.AwayFromZero);   // 83
            summed += 100 - ex;                                                      // 17
        }
        Assert.Equal(170, summed);

        // The restatement is what goes on the correction: −3p here (the tills OVER-charged).
        Assert.Equal(-3, VatAccounting.OutputTaxOn(takings, 2000) - summed);
    }
}
