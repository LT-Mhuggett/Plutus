using System;
using System.Collections.Generic;
using System.Linq;
using Plutus.SharedKernel;

namespace Plutus.Frontend.AppClient.Controls
{
    /// <summary>One column of a till table — `table-standard.md`'s `Column&lt;T&gt;`.</summary>
    /// <param name="Label">Header text.</param>
    /// <param name="Render">The cell's text. ⚠ Money goes through the caller's currency formatting;
    /// this never formats money itself, so one screen cannot show "£1.50" beside another's "1.50".</param>
    /// <param name="Numeric">Right-align, and sort as a NUMBER. ⚠ A money column sorted as text puts
    /// £100 before £9 — the whole reason the standard has a `numeric` flag.</param>
    /// <param name="SortText">The value to order by when this column is not numeric. Defaults to
    /// <paramref name="Render"/>, which is right for most columns and wrong for any whose display
    /// text hides its order (a formatted date, say) — hence the override.</param>
    /// <param name="SortNumber">The value to order by when <paramref name="Numeric"/>.</param>
    /// <param name="Sortable">Default true; set false for a column with no meaningful order.</param>
    public sealed record TableColumn<T>(
        string Label,
        Func<T, string> Render,
        bool Numeric = false,
        Func<T, string> SortText = null,
        Func<T, long> SortNumber = null,
        bool Sortable = true);

    /// <summary>
    /// What a till table is SHOWING — search, sort and page, with no UI attached.
    ///
    /// ⚠⚠ SEPARATE FROM THE CONTROL ON PURPOSE. `TillTable` cannot be exercised without a UI host,
    /// and this is where every rule that could be wrong lives: which rows match, what order they are
    /// in, which page they fall on, and what the "1–25 of 240" label says. All of it is testable
    /// here, so the control is left holding only the rendering.
    ///
    /// ⚠ The RULES come from `SharedKernel.TableSort`, shared with the web till's `DataTable.tsx`
    /// (table-standard C2). Nothing here re-derives an ordering.
    /// </summary>
    public sealed class TableView<T>
    {
        private readonly IReadOnlyList<TableColumn<T>> _columns;
        private readonly Func<T, string> _search;
        private IReadOnlyList<T> _rows = Array.Empty<T>();

        public TableView(IReadOnlyList<TableColumn<T>> columns, Func<T, string> search = null)
        {
            _columns = columns ?? throw new ArgumentNullException(nameof(columns));
            _search = search;
            PageSize = TableSort.DefaultPageSize;
        }

        public int SortColumn { get; private set; } = -1;
        public bool SortAscending { get; private set; } = true;
        public string Query { get; private set; } = string.Empty;
        public int Page { get; private set; }
        public int PageSize { get; private set; }

        /// <summary>Is there a search box at all? Omitting the accessor hides it, matching
        /// `DataTable.tsx`, where `search` is optional.</summary>
        public bool Searchable => _search != null;

        public void SetRows(IReadOnlyList<T> rows)
        {
            _rows = rows ?? Array.Empty<T>();

            // ⚠ Back to page one whenever the DATA changes. Staying on page 5 of a list that has
            // just been replaced shows an operator an arbitrary slice of something new.
            Page = 0;
        }

        /// <summary>
        /// Click a header.
        ///
        /// ⚠ Same column toggles direction; a different column starts ASCENDING. That is
        /// `DataTable.tsx`'s `setSort`, and the alternative — remembering a per-column direction —
        /// means the same click gives different answers depending on history.
        /// </summary>
        public void ToggleSort(int column)
        {
            if (column < 0 || column >= _columns.Count) return;
            if (!_columns[column].Sortable) return;

            if (SortColumn == column) SortAscending = !SortAscending;
            else { SortColumn = column; SortAscending = true; }

            Page = 0;
        }

        /// <summary>⚠ Searching returns to page one. Filtering while on page 5 and staying there is
        /// how a table shows nothing on results that exist.</summary>
        public void SetQuery(string query)
        {
            Query = query ?? string.Empty;
            Page = 0;
        }

        public void SetPageSize(int size)
        {
            PageSize = size <= 0 ? TableSort.DefaultPageSize : size;
            Page = 0;
        }

        public void NextPage() => Page++;
        public void PreviousPage() => Page = Page <= 0 ? 0 : Page - 1;

        /// <summary>Rows after searching and sorting, before paging.</summary>
        public IReadOnlyList<T> Matching()
        {
            var rows = _search is null || string.IsNullOrWhiteSpace(Query)
                ? _rows.ToList()
                : _rows.Where(r => TableSort.Matches(_search(r), Query)).ToList();

            if (SortColumn < 0 || SortColumn >= _columns.Count) return rows;

            var col = _columns[SortColumn];

            rows.Sort((a, b) =>
            {
                var result = col.Numeric && col.SortNumber != null
                    ? TableSort.Compare(col.SortNumber(a), col.SortNumber(b))
                    : TableSort.Compare(TextOf(col, a), TextOf(col, b));

                return SortAscending ? result : -result;
            });

            return rows;
        }

        /// <summary>The rows actually on screen.</summary>
        public IReadOnlyList<T> VisibleRows()
        {
            var matching = Matching();
            var (skip, take) = TableSort.Window(matching.Count, Page, PageSize);
            return matching.Skip(skip).Take(take).ToList();
        }

        /// <summary>
        /// The "1–25 of 240" counter from `table-standard.md` rule 4.
        ///
        /// ⚠ It counts the MATCHING rows, not every row loaded — an operator who has searched wants
        /// to know how many they found, and "1–3 of 240" while looking at 3 results is a lie about
        /// the filter.
        /// </summary>
        public string RangeLabel()
        {
            var total = Matching().Count;
            if (total == 0) return "0 of 0";

            var (skip, take) = TableSort.Window(total, Page, PageSize);
            return $"{skip + 1}–{skip + take} of {total}";
        }

        public bool HasPreviousPage => Page > 0;

        public bool HasNextPage
        {
            get
            {
                var total = Matching().Count;
                var (skip, take) = TableSort.Window(total, Page, PageSize);
                return skip + take < total;
            }
        }

        /// <summary>The header's sort marker — ▲ / ▼ / ⇅, as the standard specifies.</summary>
        public string SortMarkerFor(int column)
        {
            if (!_columns[column].Sortable) return string.Empty;
            if (SortColumn != column) return " ⇅";
            return SortAscending ? " ▲" : " ▼";
        }

        private static string TextOf(TableColumn<T> col, T row) =>
            col.SortText != null ? col.SortText(row) : col.Render(row);
    }
}
