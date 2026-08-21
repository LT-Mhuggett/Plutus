using System;
using System.Linq;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP-FY — the company year and the VAT periods.
///
/// ⚠⚠ THIS IS PERIOD ARITHMETIC OVER MONEY, and getting a stagger wrong does not throw: it files
/// every figure against the wrong return, silently, and the shop finds out from HMRC. The vectors
/// below are checked against a calendar rather than against the implementation.
///
/// ⚠ The three HMRC staggers, by the month a quarter ENDS:
///   stagger 1 → 3  (Mar/Jun/Sep/Dec)
///   stagger 2 → 1  (Jan/Apr/Jul/Oct)
///   stagger 3 → 2  (Feb/May/Aug/Nov)
/// </summary>
public class FinancialCalendarTests
{
    private static FinancialCalendar.VatSettings Quarterly(int endMonth, int yearStartMonth = 4) =>
        FinancialCalendar.Resolve("quarter", endMonth, yearStartMonth, 1);

    // ── Resolve ──────────────────────────────────────────────────────────────────────────────

    /// <summary>⚠⚠ NULL IS "NEVER SET", AND THE REPORT HAS TO BE ABLE TO SAY SO. A default that
    /// presents itself as a choice is a lie to somebody checking a return.</summary>
    [Fact]
    public void Nothing_stored_resolves_to_the_defaults_and_says_it_is_unconfigured()
    {
        var s = FinancialCalendar.Resolve(null, null, null, null);

        Assert.Equal("quarter", s.Basis);
        Assert.Equal(3, s.StaggerEndMonth);
        Assert.Equal(4, s.YearStartMonth);
        Assert.Equal(1, s.YearStartDay);
        Assert.False(s.Configured);
        Assert.Contains("not yet set", s.Describe());
    }

    [Fact]
    public void A_stored_quarterly_stagger_is_configured()
    {
        var s = FinancialCalendar.Resolve("quarter", 1, 4, 1);

        Assert.True(s.Configured);
        Assert.Equal(1, s.StaggerEndMonth);
        Assert.DoesNotContain("not yet set", s.Describe());
        Assert.Contains("January", s.Describe());
    }

    /// <summary>⚠ A MONTHLY FILER HAS NO STAGGER TO SET. Requiring one would report every monthly
    /// business as unconfigured for ever.</summary>
    [Fact]
    public void Monthly_is_configured_without_a_stagger()
    {
        var s = FinancialCalendar.Resolve("month", null, 4, 1);

        Assert.True(s.Configured);
        Assert.True(s.Monthly);
        Assert.Contains("Monthly", s.Describe());
    }

    /// <summary>⚠⚠ OUT OF RANGE IS TREATED AS UNSET, not clamped. A stagger of 0 or 13 sends the
    /// quarter arithmetic into a period that does not exist, and it would do it silently.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public void A_junk_stagger_is_not_a_configured_business(int stagger)
    {
        var s = FinancialCalendar.Resolve("quarter", stagger, 4, 1);

        Assert.Equal(FinancialCalendar.DefaultStaggerEndMonth, s.StaggerEndMonth);
        Assert.False(s.Configured);
    }

    [Fact]
    public void A_junk_basis_is_not_a_configured_business()
    {
        var s = FinancialCalendar.Resolve("fortnightly", 3, 4, 1);

        Assert.Equal("quarter", s.Basis);
        Assert.False(s.Configured);
    }

    /// <summary>⚠ Case and whitespace are the shapes a hand-edited row and a sloppy client produce.</summary>
    [Theory]
    [InlineData(" Quarter ")]
    [InlineData("QUARTER")]
    public void The_basis_is_read_leniently(string basis)
    {
        var s = FinancialCalendar.Resolve(basis, 3, 4, 1);

        Assert.Equal("quarter", s.Basis);
        Assert.True(s.Configured);
    }

    // ── VatPeriodOf, against a calendar ──────────────────────────────────────────────────────

    /// <summary>⚠ STAGGER 1 (ends Mar/Jun/Sep/Dec) — the common case, and the only one the old
    /// calendar-quarter picker could produce.</summary>
    [Theory]
    [InlineData("2026-01-15", "2026-01-01", "2026-03-31")]
    [InlineData("2026-03-31", "2026-01-01", "2026-03-31")]
    [InlineData("2026-04-01", "2026-04-01", "2026-06-30")]
    [InlineData("2026-12-31", "2026-10-01", "2026-12-31")]
    public void Stagger_one_buckets_on_calendar_quarters(string day, string start, string end)
    {
        var p = FinancialCalendar.VatPeriodOf(DateOnly.Parse(day), Quarterly(3));

        Assert.Equal(DateOnly.Parse(start), p.Start);
        Assert.Equal(DateOnly.Parse(end), p.End);
    }

    /// <summary>
    /// ⚠⚠ STAGGER 2 (ends Jan/Apr/Jul/Oct) — THE CASE THAT WAS UNREACHABLE. A business on this
    /// stagger files **November to January**, and `VatReturn`'s calendar-quarter picker could not
    /// produce that range at all. Note the period that crosses a year boundary: its start month is
    /// November of the PREVIOUS year, which is the negative the modulo has to normalise.
    /// </summary>
    [Theory]
    [InlineData("2026-01-15", "2025-11-01", "2026-01-31")]
    [InlineData("2025-11-01", "2025-11-01", "2026-01-31")]
    [InlineData("2026-01-31", "2025-11-01", "2026-01-31")]
    [InlineData("2026-02-01", "2026-02-01", "2026-04-30")]
    [InlineData("2026-10-31", "2026-08-01", "2026-10-31")]
    public void Stagger_two_buckets_november_to_january(string day, string start, string end)
    {
        var p = FinancialCalendar.VatPeriodOf(DateOnly.Parse(day), Quarterly(1));

        Assert.Equal(DateOnly.Parse(start), p.Start);
        Assert.Equal(DateOnly.Parse(end), p.End);
    }

    /// <summary>⚠ STAGGER 3 (ends Feb/May/Aug/Nov).</summary>
    [Theory]
    [InlineData("2026-01-15", "2025-12-01", "2026-02-28")]
    [InlineData("2026-03-01", "2026-03-01", "2026-05-31")]
    [InlineData("2026-11-30", "2026-09-01", "2026-11-30")]
    public void Stagger_three_buckets_december_to_february(string day, string start, string end)
    {
        var p = FinancialCalendar.VatPeriodOf(DateOnly.Parse(day), Quarterly(2));

        Assert.Equal(DateOnly.Parse(start), p.Start);
        Assert.Equal(DateOnly.Parse(end), p.End);
    }

    /// <summary>⚠ A LEAP FEBRUARY still ends on the 29th — the period end is computed, never assumed.</summary>
    [Fact]
    public void A_leap_february_ends_on_the_29th()
    {
        var p = FinancialCalendar.VatPeriodOf(new DateOnly(2028, 2, 10), Quarterly(2));

        Assert.Equal(new DateOnly(2028, 2, 29), p.End);
    }

    [Fact]
    public void Monthly_buckets_are_whole_calendar_months()
    {
        var s = FinancialCalendar.Resolve("month", null, 4, 1);
        var p = FinancialCalendar.VatPeriodOf(new DateOnly(2026, 2, 17), s);

        Assert.Equal(new DateOnly(2026, 2, 1), p.Start);
        Assert.Equal(new DateOnly(2026, 2, 28), p.End);
        Assert.Equal("2026-02", p.Key);
    }

    /// <summary>⚠⚠ NO DAY MAY FALL IN TWO PERIODS OR IN NONE. Swept across three years on every
    /// stagger — a gap or an overlap is takings filed twice or not at all.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Consecutive_periods_abut_exactly_with_no_gap_or_overlap(int endMonth)
    {
        var s = Quarterly(endMonth);
        var day = new DateOnly(2025, 1, 1);
        var previous = FinancialCalendar.VatPeriodOf(day, s);

        for (var d = day; d < new DateOnly(2028, 1, 1); d = d.AddDays(1))
        {
            var p = FinancialCalendar.VatPeriodOf(d, s);

            Assert.True(d >= p.Start && d <= p.End, $"{d} is outside its own period {p.Key}");

            if (p.Key == previous.Key) continue;

            // A new period must start the day after the last one ended — never a day later, never
            // a day earlier.
            Assert.Equal(previous.End.AddDays(1), p.Start);
            previous = p;
        }
    }

    // ── PeriodsOfYear — what a picker offers ─────────────────────────────────────────────────

    /// <summary>⚠ Four quarters in a financial year, and the first one contains the year's first
    /// day. This is what the `VatReturn` dropdown is built from.</summary>
    [Fact]
    public void A_quarterly_year_offers_four_periods_starting_at_the_year_start()
    {
        // Year starts 1 April; stagger 1 quarters end Mar/Jun/Sep/Dec.
        var periods = FinancialCalendar.PeriodsOfYear(Quarterly(3, yearStartMonth: 4), 2026);

        Assert.Equal(4, periods.Count);
        Assert.Equal(new DateOnly(2026, 4, 1), periods[0].Start);
        Assert.Equal(new DateOnly(2026, 6, 30), periods[0].End);
        Assert.Equal(new DateOnly(2027, 1, 1), periods[3].Start);
        Assert.Equal(new DateOnly(2027, 3, 31), periods[3].End);
    }

    /// <summary>
    /// ⚠⚠ THE CASE THE OLD PICKER GOT WRONG. A stagger-2 business whose year starts in April: its
    /// first return of FY2026 runs **February to April**, not April to June — because the day the
    /// year starts falls inside a period that began in February.
    /// </summary>
    [Fact]
    public void A_stagger_two_year_starts_mid_period_and_says_so()
    {
        var periods = FinancialCalendar.PeriodsOfYear(Quarterly(1, yearStartMonth: 4), 2026);

        Assert.Equal(new DateOnly(2026, 2, 1), periods[0].Start);
        Assert.Equal(new DateOnly(2026, 4, 30), periods[0].End);
    }

    [Fact]
    public void A_monthly_year_offers_twelve_periods()
    {
        var s = FinancialCalendar.Resolve("month", null, 1, 1);
        var periods = FinancialCalendar.PeriodsOfYear(s, 2026);

        Assert.Equal(12, periods.Count);
        Assert.Equal(new DateOnly(2026, 1, 1), periods[0].Start);
        Assert.Equal(new DateOnly(2026, 12, 31), periods[11].End);
    }

    /// <summary>⚠ The periods of a year are contiguous — the picker must not offer a list with a
    /// hole in it, which is how a quarter's takings go unfiled.</summary>
    [Fact]
    public void The_periods_of_a_year_abut()
    {
        var periods = FinancialCalendar.PeriodsOfYear(Quarterly(2, yearStartMonth: 7), 2026);

        for (var i = 1; i < periods.Count; i++)
            Assert.Equal(periods[i - 1].End.AddDays(1), periods[i].Start);
    }

    /// <summary>⚠ A year starting on the 29th of February in a non-leap year must not throw. The
    /// day is clamped to the month's length rather than rejected — a stored setting cannot be
    /// allowed to crash a report three years later.</summary>
    [Fact]
    public void A_year_start_day_past_the_end_of_the_month_is_clamped_not_thrown()
    {
        var s = FinancialCalendar.Resolve("quarter", 3, 2, 31);

        var periods = FinancialCalendar.PeriodsOfYear(s, 2027);   // February 2027 has 28 days

        Assert.NotEmpty(periods);
    }

    // ── FinancialYearOf ──────────────────────────────────────────────────────────────────────

    /// <summary>⚠ NAMED BY THE YEAR IT STARTS IN. "FY2026" for an April-start business means April
    /// 2026 → March 2027; naming it by the end year is equally common in the wild, and picking one
    /// silently is how two screens end up a year apart.</summary>
    [Theory]
    [InlineData("2026-04-01", 2026)]
    [InlineData("2026-12-31", 2026)]
    [InlineData("2027-03-31", 2026)]
    [InlineData("2027-04-01", 2027)]
    [InlineData("2026-03-31", 2025)]
    public void The_financial_year_is_named_by_the_year_it_starts_in(string day, int expected)
    {
        var s = Quarterly(3, yearStartMonth: 4);

        Assert.Equal(expected, FinancialCalendar.FinancialYearOf(DateOnly.Parse(day), s));
    }

    /// <summary>⚠ A January-start business's financial year IS the calendar year — the degenerate
    /// case, and the one a reader will assume applies to everybody.</summary>
    [Fact]
    public void A_january_start_makes_the_financial_year_the_calendar_year()
    {
        var s = Quarterly(3, yearStartMonth: 1);

        Assert.Equal(2026, FinancialCalendar.FinancialYearOf(new DateOnly(2026, 1, 1), s));
        Assert.Equal(2026, FinancialCalendar.FinancialYearOf(new DateOnly(2026, 12, 31), s));
    }
}
