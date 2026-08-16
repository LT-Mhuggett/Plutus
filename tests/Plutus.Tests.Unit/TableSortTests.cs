using System;
using System.Collections.Generic;
using System.Linq;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// How a table orders, filters and pages — `table-standard.md`, now on four surfaces.
///
/// ⚠⚠ THESE PIN A TWIN. The web till, the portal and the operator console share a byte-identical
/// `DataTable.tsx`; MAUI cannot, so this is the .NET half. If the two disagree, the SAME list orders
/// differently on a till and on the browser beside it — and that is reported as *"the till is
/// wrong"*, not as a sorting preference.
/// </summary>
public class TableSortTests
{
    // ── numeric-aware ordering: rule 1 of the standard ────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ THE CASE THE STANDARD NAMES: `"Store 2" &lt; "Store 10"`. A plain ordinal comparison puts
    /// "Store 10" first, because '1' &lt; '2'. This is the single most visible way a table looks broken.
    /// </summary>
    [Fact]
    public void Store_2_sorts_before_Store_10()
    {
        Assert.True(TableSort.Compare("Store 2", "Store 10") < 0);
        Assert.True(TableSort.Compare("Store 10", "Store 2") > 0);
    }

    [Fact]
    public void A_whole_column_of_numbered_names_sorts_the_way_a_human_reads_it()
    {
        var rows = new[] { "Till 10", "Till 2", "Till 1", "Till 20", "Till 3" };

        var sorted = rows.OrderBy(x => x, Comparer<string>.Create(TableSort.Compare)).ToArray();

        Assert.Equal(new[] { "Till 1", "Till 2", "Till 3", "Till 10", "Till 20" }, sorted);
    }

    /// <summary>⚠ Leading zeros do not make a different number — "007" and "7" are the same
    /// position, which is what `numeric: true` does and what a barcode column needs.</summary>
    [Fact]
    public void Leading_zeros_do_not_change_a_numbers_order()
    {
        Assert.Equal(0, TableSort.Compare("Item 007", "Item 7"));
        Assert.True(TableSort.Compare("Item 007", "Item 8") < 0);
    }

    [Fact]
    public void Digits_inside_a_longer_string_still_compare_as_numbers()
    {
        Assert.True(TableSort.Compare("A2B", "A10B") < 0);
        Assert.True(TableSort.Compare("2024-09", "2024-10") < 0);
    }

    // ── case and accents: sensitivity "base" ──────────────────────────────────────────────────

    /// <summary>⚠ "apple", "Apple" and "Ápple" order TOGETHER rather than in three clumps — that is
    /// `sensitivity: "base"`, and a column of names is unreadable without it.</summary>
    [Theory]
    [InlineData("apple", "APPLE")]
    [InlineData("Apple", "Ápple")]
    [InlineData("cafe", "café")]
    public void Case_and_accents_do_not_separate_otherwise_equal_text(string a, string b)
    {
        Assert.Equal(0, TableSort.Compare(a, b));
    }

    [Fact]
    public void Different_words_still_order_normally()
    {
        Assert.True(TableSort.Compare("apple", "banana") < 0);
        Assert.True(TableSort.Compare("Banana", "apple") > 0);
    }

    // ── blanks ────────────────────────────────────────────────────────────────────────────────

    /// <summary>⚠ A missing value is not a value. Blanks sort FIRST and equal to each other;
    /// scattering them through the middle makes a sorted table look unsorted.</summary>
    [Fact]
    public void Blanks_sort_first_and_together()
    {
        Assert.Equal(0, TableSort.Compare(null, ""));
        Assert.True(TableSort.Compare(null, "anything") < 0);
        Assert.True(TableSort.Compare("anything", null) > 0);
    }

    [Fact]
    public void A_shorter_string_sorts_first_once_the_shared_part_is_equal()
    {
        Assert.True(TableSort.Compare("Store 2", "Store 2A") < 0);
    }

    // ── numbers are not text ──────────────────────────────────────────────────────────────────

    /// <summary>⚠ A money column ordered as TEXT puts £100 before £9. Numeric cells get the numeric
    /// comparison, which is why the column declares itself numeric in the first place.</summary>
    [Fact]
    public void Numeric_cells_compare_as_numbers()
    {
        Assert.True(TableSort.Compare(900L, 10000L) < 0);
        Assert.True(TableSort.Compare(-500L, 0L) < 0);
    }

    // ── search ────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Batman Year One", "bat", true)]
    [InlineData("Batman Year One", "YEAR", true)]
    [InlineData("Batman Year One", "  bat  ", true)]     // the box is trimmed
    [InlineData("Batman Year One", "superman", false)]
    public void Search_is_a_case_insensitive_contains(string row, string query, bool expected)
    {
        Assert.Equal(expected, TableSort.Matches(row, query));
    }

    /// <summary>⚠ AN EMPTY BOX IS NOT A FILTER FOR NOTHING. Returning false here would empty every
    /// table the moment somebody cleared the search, which reads as "all my data has gone".</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_empty_query_matches_everything(string? query)
    {
        Assert.True(TableSort.Matches("anything at all", query));
    }

    [Fact]
    public void A_row_with_nothing_to_search_matches_nothing_but_still_does_not_throw()
    {
        Assert.False(TableSort.Matches(null, "bat"));
        Assert.True(TableSort.Matches(null, ""));
    }

    // ── paging ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_first_page_is_the_default_size_and_matches_the_web_till()
    {
        Assert.Equal(25, TableSort.DefaultPageSize);
        Assert.Equal(new[] { 25, 50, 100 }, TableSort.PageSizes);
    }

    [Theory]
    [InlineData(100, 0, 25, 0, 25)]
    [InlineData(100, 1, 25, 25, 25)]
    [InlineData(100, 3, 25, 75, 25)]
    [InlineData(30, 1, 25, 25, 5)]      // last page is short
    [InlineData(0, 0, 25, 0, 0)]        // nothing to show
    public void A_page_window_is_skip_and_take(int total, int page, int size, int skip, int take)
    {
        Assert.Equal((skip, take), TableSort.Window(total, page, size));
    }

    /// <summary>
    /// ⚠⚠ A PAGE CAN OUTLIVE ITS ROWS. Filter a 200-row table down to 3 while sitting on page 5 and
    /// an unclamped skip returns an EMPTY window — a table that looks broken, on data that is fine.
    /// The last page wins instead.
    /// </summary>
    [Fact]
    public void Paging_past_the_end_clamps_to_the_last_page_rather_than_showing_nothing()
    {
        var (skip, take) = TableSort.Window(totalRows: 3, page: 5, pageSize: 25);

        Assert.Equal(0, skip);
        Assert.Equal(3, take);
    }

    [Fact]
    public void A_negative_page_or_size_cannot_break_the_window()
    {
        Assert.Equal((0, 25), TableSort.Window(100, -1, 25));
        Assert.Equal((0, 25), TableSort.Window(100, 0, 0));
    }
}
