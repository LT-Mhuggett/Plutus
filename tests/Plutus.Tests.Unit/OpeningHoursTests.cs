using System.Linq;
using Plutus.Client.Core;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// The store's opening hours, as both tills read them.
///
/// ⚠⚠ THE ONE THAT MATTERS IS <c>Unreadable</c> vs <c>Unset</c>. Both tills printed the same sentence
/// for both states, which is how <i>"the portal shows hours and the till says there are none"</i> became
/// a report nobody could act on (Matt, 2026-08-17). Every test below asserting <c>Unreadable</c> is
/// asserting that the screen tells the truth about which of the two it is.
///
/// ⚠ C2 TWIN of the web till's <c>openingHours.test.ts</c> — the SAME VECTORS, on purpose. ⚠ And per
/// the lesson from §5b W-P7 (a ratio-first mutant that survived 32 copied vectors because
/// <c>decimal</c> and <c>double</c> round differently): these are STRING-parsing vectors, so they pin
/// the same behaviour in both languages and there is no host-arithmetic asymmetry hiding in them. That
/// is a reason to trust the mirroring HERE, not a reason to stop checking it elsewhere.
/// </summary>
public class OpeningHoursTests
{
    private const string Portal =
        "{\"mon\":[{\"open\":\"09:00\",\"close\":\"17:30\"}],\"tue\":[{\"open\":\"09:00\",\"close\":\"17:30\"}]}";

    // ── nothing stored ──

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_stored_is_unset(string? json) =>
        Assert.Equal(OpeningHoursState.Unset, OpeningHours.Parse(json).State);

    /// <summary>⚠ The portal's editor sends `null` when the last day is unticked, so this is the real
    /// "cleared" value rather than a hypothetical.</summary>
    [Fact]
    public void A_stored_literal_null_is_unset() =>
        Assert.Equal(OpeningHoursState.Unset, OpeningHours.Parse("null").State);

    [Fact]
    public void An_empty_object_is_unset() =>
        Assert.Equal(OpeningHoursState.Unset, OpeningHours.Parse("{}").State);

    // ── what the portal's simple editor writes ──

    [Fact]
    public void The_portals_own_shape_reads_and_a_missing_day_is_CLOSED()
    {
        var hours = OpeningHours.Parse(Portal);

        Assert.Equal(OpeningHoursState.Set, hours.State);
        Assert.Equal(
            new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" },
            hours.Week.Select(d => d.Label));
        Assert.Equal("09:00–17:30", OpeningHours.DayText(hours.Week[0]));
        // ⚠ CLOSED, not "unknown" and not blank — a blank row on MAUI is indistinguishable from a
        // binding to a property that does not exist.
        Assert.Equal("Closed", OpeningHours.DayText(hours.Week[2]));
    }

    /// <summary>⚠ `DayOfWeek` starts on Sunday. Using it would silently reorder every shop's week, and a
    /// week starting on the wrong day reads as a data error rather than as a bug here.</summary>
    [Fact]
    public void The_week_starts_on_Monday_whatever_order_the_blob_is_in()
    {
        var hours = OpeningHours.Parse("{\"sun\":[{\"open\":\"11:00\",\"close\":\"16:00\"}]}");

        Assert.Equal("Monday", hours.Week[0].Label);
        Assert.Equal("Sunday", hours.Week[6].Label);
        Assert.Equal("11:00–16:00", OpeningHours.DayText(hours.Week[6]));
    }

    [Fact]
    public void A_day_that_shuts_for_lunch_shows_both_spans()
    {
        var hours = OpeningHours.Parse(
            "{\"wed\":[{\"open\":\"09:00\",\"close\":\"12:30\"},{\"open\":\"13:30\",\"close\":\"17:00\"}]}");

        Assert.Equal("09:00–12:30, 13:30–17:00", OpeningHours.DayText(hours.Week[2]));
    }

    // ── ⚠ what a person types into the portal's ADVANCED JSON box ──

    [Fact]
    public void Capitalised_and_long_day_names_are_accepted()
    {
        var hours = OpeningHours.Parse(
            "{\"Monday\":[{\"open\":\"09:00\",\"close\":\"17:30\"}],\"TUE\":\"09:00-17:30\"}");

        Assert.Equal(OpeningHoursState.Set, hours.State);
        Assert.Equal("09:00–17:30", OpeningHours.DayText(hours.Week[0]));
        Assert.Equal("09:00–17:30", OpeningHours.DayText(hours.Week[1]));
    }

    [Fact]
    public void One_span_written_as_a_bare_object_is_accepted()
    {
        var hours = OpeningHours.Parse("{\"mon\":{\"open\":\"09:00\",\"close\":\"17:30\"}}");
        Assert.Equal("09:00–17:30", OpeningHours.DayText(hours.Week[0]));
    }

    [Fact]
    public void From_and_to_are_accepted_instead_of_open_and_close()
    {
        var hours = OpeningHours.Parse("{\"mon\":{\"from\":\"09:00\",\"to\":\"17:30\"}}");
        Assert.Equal("09:00–17:30", OpeningHours.DayText(hours.Week[0]));
    }

    [Fact]
    public void A_single_digit_hour_pads_and_seconds_drop()
    {
        var hours = OpeningHours.Parse("{\"mon\":{\"open\":\"9:00\",\"close\":\"17:30:00\"}}");
        Assert.Equal("09:00–17:30", OpeningHours.DayText(hours.Week[0]));
    }

    /// <summary>⚠ A range pasted out of a document carries an en dash.</summary>
    [Fact]
    public void An_en_dash_range_is_accepted()
    {
        var hours = OpeningHours.Parse("{\"mon\":\"09:00–17:30\"}");
        Assert.Equal("09:00–17:30", OpeningHours.DayText(hours.Week[0]));
    }

    [Fact]
    public void Closed_and_an_empty_list_mean_CLOSED_not_junk()
    {
        var hours = OpeningHours.Parse("{\"mon\":\"closed\",\"tue\":[]}");

        Assert.Equal(OpeningHoursState.Set, hours.State);
        Assert.Equal("Closed", OpeningHours.DayText(hours.Week[0]));
        Assert.Equal("Closed", OpeningHours.DayText(hours.Week[1]));
    }

    /// <summary>⚠ Dropping the second spelling would lose an afternoon and say nothing about it.</summary>
    [Fact]
    public void Two_spellings_of_one_day_MERGE()
    {
        var hours = OpeningHours.Parse("{\"mon\":\"09:00-12:30\",\"Monday\":\"13:30-17:00\"}");
        Assert.Equal("09:00–12:30, 13:30–17:00", OpeningHours.DayText(hours.Week[0]));
    }

    // ── ⚠⚠ stored but unreadable — the state that used to masquerade as "not set" ──

    [Fact]
    public void Malformed_JSON_is_unreadable_and_carries_the_parsers_own_message()
    {
        var hours = OpeningHours.Parse("{\"mon\":[{\"open\":\"09:00\",\"close\":\"17:30\"},]}");

        Assert.Equal(OpeningHoursState.Unreadable, hours.State);
        // ⚠ The parser's words — "invalid JSON" tells nobody which character to look at.
        Assert.False(string.IsNullOrWhiteSpace(hours.Detail));
    }

    /// <summary>⚠ What a hand-typed object looks like, and the portal's textarea accepted it.</summary>
    [Fact]
    public void Unquoted_keys_are_unreadable() =>
        Assert.Equal(OpeningHoursState.Unreadable, OpeningHours.Parse("{mon: \"09:00-17:30\"}").State);

    [Fact]
    public void A_list_where_an_object_of_days_belongs_is_unreadable()
    {
        var hours = OpeningHours.Parse("[{\"open\":\"09:00\",\"close\":\"17:30\"}]");

        Assert.Equal(OpeningHoursState.Unreadable, hours.State);
        Assert.Contains("a list", hours.Detail);
    }

    [Theory]
    [InlineData("\"09:00-17:30\"")]
    [InlineData("42")]
    [InlineData("true")]
    public void A_bare_value_is_unreadable(string json) =>
        Assert.Equal(OpeningHoursState.Unreadable, OpeningHours.Parse(json).State);

    [Fact]
    public void Keys_that_are_not_days_at_all_are_unreadable()
    {
        var hours = OpeningHours.Parse("{\"weekdays\":\"09:00-17:30\",\"weekend\":\"closed\"}");

        Assert.Equal(OpeningHoursState.Unreadable, hours.State);
        Assert.Contains("weekdays", hours.Detail);
    }

    [Fact]
    public void Days_whose_values_carry_no_times_are_unreadable()
    {
        var hours = OpeningHours.Parse("{\"mon\":{\"opens\":\"09:00\"},\"tue\":{\"opens\":\"09:00\"}}");

        Assert.Equal(OpeningHoursState.Unreadable, hours.State);
        Assert.Contains("times", hours.Detail);
    }

    // ── leftover keys are named, not dropped ──

    /// <summary>⚠ A key nobody reads is a setting somebody believes is in effect — bank holidays here.</summary>
    [Fact]
    public void Understood_days_render_and_the_rest_are_named()
    {
        const string json = "{\"mon\":\"09:00-17:30\",\"holidays\":\"closed\"}";

        Assert.Equal(OpeningHoursState.Set, OpeningHours.Parse(json).State);
        Assert.Equal(new[] { "holidays" }, OpeningHours.UnknownDayKeys(json));
    }

    [Fact]
    public void A_clean_blob_has_nothing_to_name() =>
        Assert.Empty(OpeningHours.UnknownDayKeys(Portal));

    [Fact]
    public void Junk_has_nothing_to_name_because_the_unreadable_message_covers_it() =>
        Assert.Empty(OpeningHours.UnknownDayKeys("{oops"));
}
