using System;

namespace Plutus.SharedKernel;

/// <summary>
/// **How a business's financial year and VAT periods are worked out — the ONE rule.**
///
/// ⚠⚠ WP-FY, 2026-08-21. Matt: *"I need to be able to set the company year in the portal. And the
/// VAT periods. This then needs to be reflected in the reports, specifically the VAT reports needs
/// to match the months it reports on."*
///
/// ⚠⚠ **THE BUG THIS EXISTS TO CLOSE WAS NOT A MISSING SETTING — IT WAS TWO SCREENS DISAGREEING.**
/// `vat-corrections` took `basis` and `staggerEndMonth` off the QUERY STRING with defaults, so the
/// shop's real stagger was whatever the caller last typed. `VatReturn`, the screen an accountant
/// actually files from, took neither: its quarter picker was hard-wired to CALENDAR quarters
/// (Q1 = Jan–Mar). A business on stagger 2 files Nov–Jan and **could not produce that range on that
/// screen at all** — and nothing on it said so.
///
/// ⚠ IN `SharedKernel` SO THE TILL CAN ASK TOO. A till showing "this VAT period" must land on the
/// same boundary the portal files on, and a second implementation would be a C2 twin over money.
///
/// ⚠ NO I/O AND NO CLOCK. Everything here is a pure function of the settings and a date, so the
/// stagger arithmetic is unit-testable without a database — getting a stagger wrong silently files
/// every figure against the wrong return, which is not a thing to find out in production.
/// </summary>
public static class FinancialCalendar
{
    /// <summary>⚠ HMRC's most common stagger: quarters ending Mar/Jun/Sep/Dec.</summary>
    public const int DefaultStaggerEndMonth = 3;

    /// <summary>⚠ Most retailers file quarterly. The other legal value is <c>month</c>.</summary>
    public const string DefaultBasis = "quarter";

    /// <summary>⚠ April, the UK default for a company year — and the month a year starting "1 April"
    /// begins, not the month it ends.</summary>
    public const int DefaultYearStartMonth = 4;

    /// <summary>
    /// What a business's VAT settings actually are, and whether anybody chose them.
    /// </summary>
    /// <param name="Basis"><c>quarter</c> or <c>month</c>.</param>
    /// <param name="StaggerEndMonth">The month a quarter ENDS, 1–12. Meaningless when monthly.</param>
    /// <param name="YearStartMonth">Month the financial year starts, 1–12.</param>
    /// <param name="YearStartDay">Day of that month, 1–31.</param>
    /// <param name="Configured">⚠⚠ FALSE MEANS NOBODY HAS SET THIS. A report must say so: a shop
    /// reading a VAT return needs to know whether the period is the one they file on or the one
    /// nobody has told us about, and a default that presents itself as a choice is a lie.</param>
    public sealed record VatSettings(
        string Basis,
        int StaggerEndMonth,
        int YearStartMonth,
        int YearStartDay,
        bool Configured)
    {
        public bool Monthly => Basis == "month";

        /// <summary>
        /// Words for a report header — ⚠ **including whether it was chosen**.
        ///
        /// ⚠ REQUIREMENT 4 OF WP-FY: *"the report says which basis it used, on screen"*. A VAT
        /// report that does not name its period basis cannot be checked against a filed return,
        /// which is the only thing it is for.
        /// </summary>
        public string Describe() =>
            (Monthly ? "Monthly VAT periods" : $"Quarterly VAT periods, ending {MonthName(StaggerEndMonth)}")
            + (Configured ? "" : " — the default, not yet set in the portal");
    }

    /// <summary>
    /// Resolve what a business filed on, from whatever it has stored.
    ///
    /// ⚠⚠ EVERY VALUE IS RANGE-CHECKED, not merely null-checked. These columns are nullable ints
    /// that a bad request or a hand-edited row could set to 0 or 13, and a stagger of 0 sends
    /// `VatPeriodOf` into a quarter that does not exist — silently, producing a period key nothing
    /// else in the estate agrees with. Out of range is treated exactly like unset.
    ///
    /// ⚠ `Configured` IS TRUE ONLY IF SOMETHING USABLE WAS STORED. A row with a junk stagger is not
    /// a configured business, and reporting it as one would hide the very thing that needs fixing.
    /// </summary>
    public static VatSettings Resolve(
        string? basis, int? staggerEndMonth, int? yearStartMonth, int? yearStartDay)
    {
        var cleanBasis = basis?.Trim().ToLowerInvariant();
        var okBasis = cleanBasis is "quarter" or "month";

        var okStagger = staggerEndMonth is >= 1 and <= 12;
        var okYearMonth = yearStartMonth is >= 1 and <= 12;
        var okYearDay = yearStartDay is >= 1 and <= 31;

        return new VatSettings(
            okBasis ? cleanBasis! : DefaultBasis,
            okStagger ? staggerEndMonth!.Value : DefaultStaggerEndMonth,
            okYearMonth ? yearStartMonth!.Value : DefaultYearStartMonth,
            okYearDay ? yearStartDay!.Value : 1,
            // ⚠ A MONTHLY filer has no stagger to set, so requiring one would report every monthly
            // business as unconfigured forever.
            okBasis && (cleanBasis == "month" || okStagger));
    }

    /// <summary>
    /// The VAT period a business day falls in.
    ///
    /// ⚠⚠ THIS IS `ReportsController.VatPeriodOf`'S ARITHMETIC, MOVED HERE UNCHANGED — same modulo,
    /// same normalisation for a stagger whose quarter starts in the previous year (stagger 2 ends in
    /// January, so its start month is "-1" = November). It was correct and it was **private to one
    /// controller**, which is why the screen an accountant files from never used it.
    /// </summary>
    public static (string Key, DateOnly Start, DateOnly End) VatPeriodOf(DateOnly day, VatSettings s)
    {
        if (s.Monthly)
        {
            var start = new DateOnly(day.Year, day.Month, 1);
            return ($"{day.Year:0000}-{day.Month:00}", start, start.AddMonths(1).AddDays(-1));
        }

        // A quarter ending in month E starts at E-2. How many months is this day into its own
        // quarter? Modulo 3 off that start, normalised for negatives.
        var monthsIn = ((day.Month - (s.StaggerEndMonth - 2)) % 3 + 3) % 3;
        var qStart = new DateOnly(day.Year, day.Month, 1).AddMonths(-monthsIn);
        var qEnd = qStart.AddMonths(3).AddDays(-1);
        return ($"{qStart.Year:0000}-{qStart.Month:00}..{qEnd.Year:0000}-{qEnd.Month:00}", qStart, qEnd);
    }

    /// <summary>
    /// The four (or twelve) periods of a financial year, oldest first — what a period PICKER offers.
    ///
    /// ⚠⚠ THIS IS WHAT `VatReturn`'S QUARTER DROPDOWN WAS MISSING. It offered "Q1–Q4" meaning
    /// calendar quarters, so a stagger-2 business picking "Q1" got Jan–Mar when their first return
    /// of the year is Nov–Jan. The options now come from the stagger, and each one is LABELLED WITH
    /// ITS ACTUAL MONTHS so nobody has to trust a number.
    ///
    /// ⚠ ANCHORED ON THE FINANCIAL YEAR, not the calendar year — that is what "the company year"
    /// being a setting is FOR. A business whose year starts in April and files on stagger 1 gets
    /// Apr–Jun first, not Jan–Mar.
    /// </summary>
    public static IReadOnlyList<(string Key, DateOnly Start, DateOnly End)> PeriodsOfYear(
        VatSettings s, int financialYear)
    {
        var result = new List<(string, DateOnly, DateOnly)>();

        // ⚠ `financialYear` NAMES THE YEAR THE FINANCIAL YEAR STARTS IN. "FY2026" for a business
        // starting in April means April 2026 → March 2027, which is how a UK business says it. The
        // alternative (naming it by the end year) is equally common in the wild and picking one
        // silently is how two screens end up a year apart.
        var yearStart = new DateOnly(financialYear, s.YearStartMonth, Math.Min(s.YearStartDay, DateTime.DaysInMonth(financialYear, s.YearStartMonth)));
        var yearEnd = yearStart.AddYears(1).AddDays(-1);

        // Walk periods from the one containing the year's first day until we pass its last.
        var cursor = VatPeriodOf(yearStart, s);

        while (cursor.Start <= yearEnd)
        {
            result.Add(cursor);
            cursor = VatPeriodOf(cursor.End.AddDays(1), s);

            // ⚠ A BACKSTOP, not decoration. If the arithmetic above ever stopped advancing this
            // would spin forever inside a request; twelve monthly periods plus a margin is the most
            // any legal configuration can produce.
            if (result.Count > 16) break;
        }

        return result;
    }

    /// <summary>Which financial year a day belongs to, named by the year it STARTS in.</summary>
    public static int FinancialYearOf(DateOnly day, VatSettings s)
    {
        var startThisYear = new DateOnly(day.Year, s.YearStartMonth, Math.Min(s.YearStartDay, DateTime.DaysInMonth(day.Year, s.YearStartMonth)));
        return day < startThisYear ? day.Year - 1 : day.Year;
    }

    private static string MonthName(int month) =>
        month is >= 1 and <= 12
            ? System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month)
            : "?";
}
