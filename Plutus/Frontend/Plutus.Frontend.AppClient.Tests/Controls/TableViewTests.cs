using System;
using System.Collections.Generic;
using System.Linq;
using Plutus.Frontend.AppClient.Controls;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Controls
{
    /// <summary>
    /// What a till table is SHOWING — `table-standard.md`'s four rules, on the fourth surface.
    ///
    /// ⚠⚠ THIS IS THE HALF THAT CAN BE TESTED. `TillTable` needs a UI host; everything that could be
    /// WRONG — which rows match, their order, which page they land on, what the counter says — lives
    /// here instead, so the control is left holding only the rendering.
    /// </summary>
    public class TableViewTests
    {
        private sealed record Row(string Name, long Pence);

        private static readonly TableColumn<Row>[] Columns =
        {
            new("Name", r => r.Name),
            new("Total", r => $"{r.Pence / 100m:0.00}", Numeric: true, SortNumber: r => r.Pence),
            new("Fixed", r => "-", Sortable: false),
        };

        private static TableView<Row> View(params Row[] rows)
        {
            var v = new TableView<Row>(Columns, search: r => r.Name);
            v.SetRows(rows);
            return v;
        }

        private static Row[] Sample() => new[]
        {
            new Row("Till 10", 900),
            new Row("Till 2", 10_000),
            new Row("Till 1", 50),
        };

        // ── rule 1: orderable, numeric-aware ──────────────────────────────────────────────────

        /// <summary>⚠ THE HEADLINE CASE, through the control's own path rather than the comparator's:
        /// "Till 2" before "Till 10".</summary>
        [Fact]
        public void Sorting_by_a_text_column_is_numeric_aware()
        {
            var v = View(Sample());
            v.ToggleSort(0);

            Assert.Equal(new[] { "Till 1", "Till 2", "Till 10" }, v.VisibleRows().Select(r => r.Name));
        }

        /// <summary>⚠ A MONEY COLUMN SORTS AS A NUMBER. As text, "10000" would come before "900" —
        /// £100 before £9, which is the defect the `numeric` flag exists for.</summary>
        [Fact]
        public void Sorting_by_a_numeric_column_uses_the_number_not_the_text()
        {
            var v = View(Sample());
            v.ToggleSort(1);

            Assert.Equal(new long[] { 50, 900, 10_000 }, v.VisibleRows().Select(r => r.Pence));
        }

        [Fact]
        public void Clicking_the_same_header_reverses_and_a_different_one_starts_ascending()
        {
            var v = View(Sample());

            v.ToggleSort(0);
            Assert.True(v.SortAscending);

            v.ToggleSort(0);
            Assert.False(v.SortAscending);
            Assert.Equal("Till 10", v.VisibleRows()[0].Name);

            // ⚠ A DIFFERENT column starts ascending — not "whatever the last one was". Remembering a
            // per-column direction makes the same click give different answers depending on history.
            v.ToggleSort(1);
            Assert.True(v.SortAscending);
        }

        [Fact]
        public void A_column_marked_unsortable_ignores_the_click()
        {
            var v = View(Sample());
            v.ToggleSort(2);

            Assert.Equal(-1, v.SortColumn);
            Assert.Equal(string.Empty, v.SortMarkerFor(2));
        }

        /// <summary>⚠ The marker is ⇅ until a column is sorted, then ▲/▼ — rule 1's affordance. An
        /// operator cannot sort a table they cannot tell is sortable.</summary>
        [Fact]
        public void The_header_marker_says_whether_and_how_a_column_is_sorted()
        {
            var v = View(Sample());

            Assert.Equal(" ⇅", v.SortMarkerFor(0));
            v.ToggleSort(0);
            Assert.Equal(" ▲", v.SortMarkerFor(0));
            v.ToggleSort(0);
            Assert.Equal(" ▼", v.SortMarkerFor(0));
            Assert.Equal(" ⇅", v.SortMarkerFor(1));
        }

        // ── rule 2: searchable ────────────────────────────────────────────────────────────────

        [Fact]
        public void Searching_filters_case_insensitively()
        {
            var v = View(Sample());
            v.SetQuery("till 1");

            Assert.Equal(new[] { "Till 10", "Till 1" }, v.VisibleRows().Select(r => r.Name));
        }

        /// <summary>⚠ CLEARING THE BOX RESTORES EVERYTHING. Treating an empty query as "match
        /// nothing" empties every table the moment somebody clears it, which reads as data loss.</summary>
        [Fact]
        public void Clearing_the_search_box_brings_every_row_back()
        {
            var v = View(Sample());
            v.SetQuery("till 1");
            v.SetQuery("");

            Assert.Equal(3, v.VisibleRows().Count);
        }

        /// <summary>⚠ A table with no search accessor has no search box — `DataTable.tsx` makes
        /// `search` optional for the same reason: a box that filters nothing is worse than none.</summary>
        [Fact]
        public void A_table_with_no_search_accessor_is_not_searchable()
        {
            var v = new TableView<Row>(Columns);
            v.SetRows(Sample());
            v.SetQuery("till 1");

            Assert.False(v.Searchable);
            Assert.Equal(3, v.VisibleRows().Count);
        }

        // ── rules 3 & 4: page size and pagination ─────────────────────────────────────────────

        [Fact]
        public void The_default_page_size_is_25_and_pages_hold_that_many()
        {
            var v = new TableView<Row>(Columns);
            v.SetRows(Enumerable.Range(1, 60).Select(i => new Row($"R{i}", i)).ToList());

            Assert.Equal(25, v.PageSize);
            Assert.Equal(25, v.VisibleRows().Count);
            Assert.Equal("1–25 of 60", v.RangeLabel());

            v.NextPage();
            Assert.Equal("26–50 of 60", v.RangeLabel());

            v.NextPage();
            Assert.Equal(10, v.VisibleRows().Count);
            Assert.Equal("51–60 of 60", v.RangeLabel());
            Assert.False(v.HasNextPage);
        }

        [Fact]
        public void Prev_and_next_cannot_run_off_either_end()
        {
            var v = new TableView<Row>(Columns);
            v.SetRows(Enumerable.Range(1, 30).Select(i => new Row($"R{i}", i)).ToList());

            Assert.False(v.HasPreviousPage);
            v.PreviousPage();
            Assert.Equal("1–25 of 30", v.RangeLabel());

            v.NextPage();
            v.NextPage();          // past the end
            Assert.Equal("26–30 of 30", v.RangeLabel());
        }

        /// <summary>
        /// ⚠⚠ SEARCHING RETURNS TO PAGE ONE. Filter while sitting on page 3 and stay there, and the
        /// table shows NOTHING on results that exist — the single most convincing way to look broken.
        /// </summary>
        [Fact]
        public void Searching_returns_to_the_first_page()
        {
            var v = new TableView<Row>(Columns, r => r.Name);
            v.SetRows(Enumerable.Range(1, 60).Select(i => new Row($"R{i}", i)).ToList());

            v.NextPage();
            v.NextPage();
            v.SetQuery("R7");

            Assert.Equal(0, v.Page);
            Assert.NotEmpty(v.VisibleRows());
        }

        [Fact]
        public void Changing_the_page_size_or_the_rows_also_returns_to_the_first_page()
        {
            var v = new TableView<Row>(Columns);
            v.SetRows(Enumerable.Range(1, 60).Select(i => new Row($"R{i}", i)).ToList());

            v.NextPage();
            v.SetPageSize(50);
            Assert.Equal(0, v.Page);
            Assert.Equal("1–50 of 60", v.RangeLabel());

            v.NextPage();
            v.SetRows(new[] { new Row("only", 1) });
            Assert.Equal(0, v.Page);
        }

        /// <summary>
        /// ⚠ THE COUNTER COUNTS WHAT WAS FOUND, not everything loaded. "1–3 of 240" while looking at
        /// three search results is a lie about the filter, and an operator reads it as the search
        /// having failed.
        /// </summary>
        [Fact]
        public void The_counter_reports_the_matching_rows_not_the_loaded_ones()
        {
            var v = new TableView<Row>(Columns, r => r.Name);
            v.SetRows(Enumerable.Range(1, 60).Select(i => new Row($"R{i}", i)).ToList());

            v.SetQuery("R60");

            Assert.Equal("1–1 of 1", v.RangeLabel());
        }

        [Fact]
        public void An_empty_table_says_zero_rather_than_throwing()
        {
            var v = new TableView<Row>(Columns, r => r.Name);
            v.SetRows(Array.Empty<Row>());

            Assert.Empty(v.VisibleRows());
            Assert.Equal("0 of 0", v.RangeLabel());
            Assert.False(v.HasNextPage);
            Assert.False(v.HasPreviousPage);
        }

        [Fact]
        public void Null_rows_are_treated_as_an_empty_table()
        {
            var v = new TableView<Row>(Columns);
            v.SetRows(null);

            Assert.Empty(v.VisibleRows());
        }

        // ── the two together ──────────────────────────────────────────────────────────────────

        /// <summary>⚠ Sorting applies to the SEARCH RESULTS, not to the page — sorting a page in
        /// isolation reorders 25 rows and leaves the other 200 where they were.</summary>
        [Fact]
        public void Sorting_orders_every_matching_row_not_just_the_visible_page()
        {
            var v = new TableView<Row>(Columns, r => r.Name);
            v.SetRows(Enumerable.Range(1, 60).Select(i => new Row($"R{i}", i * 100)).ToList());

            v.ToggleSort(1);
            v.ToggleSort(1);   // descending — the biggest first

            Assert.Equal(6000, v.VisibleRows()[0].Pence);
        }
    }
}
