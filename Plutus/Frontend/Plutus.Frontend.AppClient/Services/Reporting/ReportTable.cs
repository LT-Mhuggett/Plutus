using System;
using System.Collections.Generic;
using System.Linq;

namespace Plutus.Frontend.AppClient.Services.Reporting
{
    /// <summary>
    /// One cell of a report.
    ///
    /// ⚠ <see cref="SortNumber"/> IS WHY THIS TYPE EXISTS. A report table is rendered generically, so
    /// the row objects are erased to text — and text sorts "£100" before "£9". The cell therefore
    /// carries the number it was made from, and the table sorts on that.
    /// </summary>
    /// <param name="Text">What the operator reads. ⚠ Already formatted — money through the caller's
    /// currency formatting, so one report cannot show "£1.50" beside another's "1.50".</param>
    /// <param name="Numeric">Right-align, and sort by <paramref name="SortNumber"/>.</param>
    /// <param name="SortNumber">The underlying value for a numeric cell — pence, a count, a rank.</param>
    public sealed record ReportCell(string Text, bool Numeric = false, long SortNumber = 0);

    /// <summary>One row of a report — cells in column order.</summary>
    public sealed record ReportRow(IReadOnlyList<ReportCell> Cells)
    {
        /// <summary>What the search box matches against. ⚠ Every cell, so an operator can find a row
        /// by anything they can see on it rather than guessing which column is searchable.</summary>
        public string SearchText => string.Join(" ", Cells.Select(c => c.Text));

        public ReportCell Cell(int index) =>
            index >= 0 && index < Cells.Count ? Cells[index] : new ReportCell(string.Empty);
    }

    /// <summary>
    /// A whole report, ready to render — headers, rows, and anything that must be SAID about them.
    ///
    /// ⚠⚠ <see cref="Note"/> IS NOT DECORATION. It is where a report admits a server cap
    /// ("2,000 of 5,000 lines shown"), an empty range, or a permission refusal. Step 26 requires the
    /// caps be visible: a truncated report whose total looks complete is worse than no report, and
    /// it is the one thing a generic renderer cannot work out for itself.
    /// </summary>
    /// <param name="Totals">A summary line shown above the table — already formatted. Optional.</param>
    public sealed record ReportTable(
        IReadOnlyList<string> Headers,
        IReadOnlyList<bool> NumericColumns,
        IReadOnlyList<ReportRow> Rows,
        string Note = "",
        string Totals = "")
    {
        public static ReportTable Empty(string note) =>
            new(Array.Empty<string>(), Array.Empty<bool>(), Array.Empty<ReportRow>(), note);

        /// <summary>Build a table from typed rows — the shape every report definition uses.</summary>
        public static ReportTable From<T>(
            IEnumerable<T> rows,
            IReadOnlyList<string> headers,
            IReadOnlyList<bool> numeric,
            Func<T, IReadOnlyList<ReportCell>> cells,
            string note = "",
            string totals = "")
        {
            if (headers.Count != numeric.Count)
                throw new ArgumentException(
                    $"A report declares {headers.Count} headers but {numeric.Count} numeric flags. " +
                    "They are positional, so a mismatch silently right-aligns the wrong column and " +
                    "sorts it as text.", nameof(numeric));

            return new ReportTable(
                headers,
                numeric,
                (rows ?? Enumerable.Empty<T>()).Select(r => new ReportRow(cells(r))).ToList(),
                note,
                totals);
        }
    }
}
