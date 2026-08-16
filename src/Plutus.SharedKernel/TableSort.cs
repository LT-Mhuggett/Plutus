using System;
using System.Globalization;

namespace Plutus.SharedKernel;

/// <summary>
/// How a data table orders and pages its rows — the `table-standard.md` rules, shared.
///
/// ⚠⚠ IT IS HERE BECAUSE MAUI IS THE FOURTH SURFACE. `table-standard.md` was written for three
/// (web till, portal, operator console), all React, all sharing a byte-identical `DataTable.tsx`.
/// MAUI cannot share that file, so without this the same list would order differently on a till than
/// on the browser beside it — and "Store 10 before Store 2" is the kind of difference an operator
/// reports as *"the till is wrong"*.
///
/// ⚠ Matt, 2026-08-16, on the reporting rebuild: **"Tables only."** Same table behaviours as the web
/// till — orderable, searchable, page-sized, paginated — and no chart, the portal being the right
/// home for those.
/// </summary>
public static class TableSort
{
    /// <summary>The page sizes every table offers; the FIRST is the default.
    /// ⚠ Matches `DataTable.tsx`'s `PAGE_SIZES` — a till showing 20 where the browser shows 25 makes
    /// two people comparing screens disagree about what is "on the first page".</summary>
    public static readonly int[] PageSizes = { 25, 50, 100 };

    public static int DefaultPageSize => PageSizes[0];

    /// <summary>
    /// Order two cell values the way the web till does.
    ///
    /// ⚠⚠ NUMERIC-AWARE, WHICH IS THE WHOLE POINT: `"Store 2"` sorts before `"Store 10"`. A plain
    /// ordinal comparison puts "Store 10" first because '1' &lt; '2', and that is precisely the
    /// complaint `table-standard.md` rule 1 exists to prevent.
    ///
    /// ⚠ Case- and accent-insensitive, matching `sensitivity: "base"` — "apple", "Apple" and
    /// "Ápple" order together rather than in three separate clumps.
    ///
    /// ⚠ Null and empty sort FIRST and equal to each other. A missing value is not a value; scattering
    /// blanks through the middle of a sorted column makes a table look unsorted.
    /// </summary>
    public static int Compare(string? a, string? b)
    {
        var left = a ?? string.Empty;
        var right = b ?? string.Empty;

        int i = 0, j = 0;

        while (i < left.Length && j < right.Length)
        {
            if (char.IsDigit(left[i]) && char.IsDigit(right[j]))
            {
                // ⚠ Compare digit RUNS as numbers, not characters — and skip leading zeros so
                // "007" and "7" compare equal, as `numeric: true` does.
                var (leftDigits, nextI) = DigitRun(left, i);
                var (rightDigits, nextJ) = DigitRun(right, j);

                if (leftDigits.Length != rightDigits.Length)
                    return leftDigits.Length < rightDigits.Length ? -1 : 1;

                var runCompare = string.CompareOrdinal(leftDigits, rightDigits);
                if (runCompare != 0) return runCompare < 0 ? -1 : 1;

                i = nextI;
                j = nextJ;
                continue;
            }

            var charCompare = CompareChars(left[i], right[j]);
            if (charCompare != 0) return charCompare;

            i++;
            j++;
        }

        // ⚠ The shorter string sorts first once every shared chunk is equal — "Store 2" before
        // "Store 2A".
        var remaining = (left.Length - i) - (right.Length - j);
        return remaining == 0 ? 0 : remaining < 0 ? -1 : 1;
    }

    /// <summary>Compare two numeric cells. ⚠ Separate from the string path because a table column
    /// of money must not be ordered as text — "£100" before "£9" is the same defect one layer up.</summary>
    public static int Compare(long a, long b) => a.CompareTo(b);

    /// <summary>
    /// Does this row match the search box?
    ///
    /// ⚠ Case-insensitive `Contains`, matching `DataTable.tsx`'s
    /// `search(r).toLowerCase().includes(q)` — an operator typing "bat" must find "Batman".
    /// ⚠ An empty or whitespace query matches EVERYTHING; it is not a filter for nothing.
    /// </summary>
    public static bool Matches(string? haystack, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        if (string.IsNullOrEmpty(haystack)) return false;

        return CultureInfo.InvariantCulture.CompareInfo.IndexOf(
            haystack, query.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    }

    /// <summary>
    /// The window of rows for a page — "X–Y of N" in `table-standard.md` terms.
    ///
    /// ⚠ CLAMPED, because a page can outlive its rows: filter a 200-row table down to 3 while sitting
    /// on page 5 and an unclamped skip shows an empty table that looks broken. The last page wins.
    /// </summary>
    public static (int Skip, int Take) Window(int totalRows, int page, int pageSize)
    {
        if (pageSize <= 0) pageSize = DefaultPageSize;
        if (totalRows <= 0) return (0, 0);

        var lastPage = (totalRows - 1) / pageSize;
        var safePage = page < 0 ? 0 : page > lastPage ? lastPage : page;

        var skip = safePage * pageSize;
        return (skip, Math.Min(pageSize, totalRows - skip));
    }

    private static (string Digits, int Next) DigitRun(string s, int from)
    {
        var i = from;
        while (i < s.Length && char.IsDigit(s[i])) i++;

        // Leading zeros dropped so "007" == "7"; an all-zero run keeps one digit.
        var run = s.Substring(from, i - from).TrimStart('0');
        return (run.Length == 0 ? "0" : run, i);
    }

    private static int CompareChars(char a, char b)
    {
        var result = CultureInfo.InvariantCulture.CompareInfo.Compare(
            a.ToString(), b.ToString(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);

        return result == 0 ? 0 : result < 0 ? -1 : 1;
    }
}
