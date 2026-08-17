using System.Linq;
using Plutus.Client.Core;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// The portal's theme, as a till reads it (FE10 / step 22).
///
/// ⚠⚠ THIS IS A C2 TWIN AND THAT IS WHY THE TESTS ARE SHAPED LIKE THIS. Matt, 2026-08-17:
/// *"Changing the theme in the portal needs to be consistent across the web and MAUI till."* The web
/// till's half is `theme.ts` (TypeScript — the code cannot literally be shared), so every case below is
/// written against a rule that exists twice, and each one names the `theme.ts` behaviour it mirrors.
///
/// ⚠ `colorsJson` is an **opaque blob the server does not validate**. That makes this the only thing
/// between a bad value in it and an unreadable till.
/// </summary>
public class ThemeSlotsTests
{
    // ── the seven slots ───────────────────────────────────────────────────────────────────────

    /// <summary>⚠ Seven, these names, this order — matching `theme.ts SLOT_VARS`. A slot added on one
    /// till and not the other is a colour that lands in a shop on one screen only.</summary>
    [Fact]
    public void The_seven_slots_are_the_web_tills_seven()
    {
        Assert.Equal(
            new[] { "accent", "accentInk", "surface", "surface2", "ink", "inkMuted", "line" },
            ThemeSlots.Names);
    }

    /// <summary>⚠ The stock accent is the palette's `Quinary` **and** the web till's `DEFAULT_ACCENT`.
    /// Both tills must land on the same colour when every override is cleared.</summary>
    [Fact]
    public void The_default_accent_matches_the_web_till() =>
        Assert.Equal("#2c698d", ThemeSlots.DefaultAccent);

    // ── what counts as a colour ───────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ SIX-DIGIT HEX ONLY — the web till calls this *"the till's last line of defence against a bad
    /// value in the blob"*. Shorthand `#abc` and 8-digit `#rrggbbaa` are both refused: an accepted
    /// alpha channel could render a till's text at zero opacity.
    /// </summary>
    [Theory]
    [InlineData("#2c698d", true)]
    [InlineData("#FFFFFF", true)]
    [InlineData("#abcdef", true)]
    [InlineData("#ABC", false)]           // shorthand
    [InlineData("#2c698dff", false)]      // alpha
    [InlineData("2c698d", false)]         // no hash
    [InlineData("rebeccapurple", false)]  // named
    [InlineData("rgb(44,105,141)", false)]
    [InlineData("#2c698", false)]         // five
    [InlineData("#gggggg", false)]        // not hex
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_six_digit_hex_is_usable(string? value, bool usable) =>
        Assert.Equal(usable, ThemeSlots.IsUsableColour(value));

    // ── reading the blob ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Every_valid_slot_is_read()
    {
        var json = """
        {"accent":"#111111","accentInk":"#222222","surface":"#333333",
         "surface2":"#444444","ink":"#555555","inkMuted":"#666666","line":"#777777"}
        """;

        var colours = ThemeSlots.ColoursFrom(json);

        Assert.Equal(7, colours.Count);
        Assert.Equal("#111111", colours["accent"]);
        Assert.Equal("#777777", colours["line"]);
    }

    /// <summary>
    /// ⚠⚠ A BAD SLOT IS ABSENT, NOT SUBSTITUTED. The web till `removeProperty`s it so the stylesheet's
    /// own default shows; MAUI leaves the stock palette key untouched for the same reason. **Guessing a
    /// replacement is how you get white text on a white surface** — and the caller cannot tell a
    /// guessed colour from an intended one.
    /// </summary>
    [Fact]
    public void A_bad_colour_is_left_out_rather_than_replaced()
    {
        var colours = ThemeSlots.ColoursFrom("""{"accent":"#111111","ink":"not-a-colour"}""");

        Assert.True(colours.ContainsKey("accent"));
        Assert.False(colours.ContainsKey("ink"));
    }

    /// <summary>⚠ And a slot the portal simply did not send is absent too — the two cases are the same
    /// case, deliberately, so "cleared" and "invalid" both fall back to stock.</summary>
    [Fact]
    public void An_absent_slot_is_absent()
    {
        var colours = ThemeSlots.ColoursFrom("""{"accent":"#111111"}""");

        Assert.Single(colours);
        Assert.False(colours.ContainsKey("surface"));
    }

    /// <summary>
    /// ⚠⚠ THE ONE THAT MATTERS MOST — a malformed blob must not lose the BASE MODE. `theme.ts` says it
    /// outright: *"opaque blob was bad — base mode still applies"*. Light/dark is useful on its own, and
    /// a shop set to dark should not be thrown back to light by one bad hex.
    /// </summary>
    [Theory]
    [InlineData("not json at all")]
    [InlineData("{ broken")]
    [InlineData("[]")]                    // valid JSON, wrong shape
    [InlineData("null")]
    [InlineData("\"a string\"")]
    public void A_malformed_blob_yields_no_colours_and_never_throws(string json)
    {
        Assert.Empty(ThemeSlots.ColoursFrom(json));

        // ⚠ And the caller can still apply the mode — proven here because that is the whole reason
        // this returns empty instead of throwing.
        Assert.Equal(ThemeBaseMode.Dark, ThemeSlots.ModeFrom("dark"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_blob_is_no_colours(string? json) => Assert.Empty(ThemeSlots.ColoursFrom(json));

    /// <summary>⚠ An UNKNOWN slot is ignored, not an error — a later portal may send an eighth, and an
    /// older till must keep working rather than lose the seven it understands.</summary>
    [Fact]
    public void An_unknown_slot_is_ignored_and_the_known_ones_still_apply()
    {
        var colours = ThemeSlots.ColoursFrom("""{"accent":"#111111","sparkle":"#999999"}""");

        Assert.Single(colours);
        Assert.Equal("#111111", colours["accent"]);
    }

    /// <summary>⚠ Slot names are matched EXACTLY. The blob is written against the web till's camelCase
    /// `ThemeColors`, so accepting `Accent` here would apply a colour on MAUI that the browser ignores.</summary>
    [Theory]
    [InlineData("""{"Accent":"#111111"}""")]
    [InlineData("""{"ACCENT":"#111111"}""")]
    [InlineData("""{"accent_ink":"#111111"}""")]
    public void Slot_names_are_case_and_spelling_exact(string json) =>
        Assert.Empty(ThemeSlots.ColoursFrom(json));

    /// <summary>⚠ A non-string value is not a colour — the portal storing a number must not become a
    /// crash on a shop floor.</summary>
    [Fact]
    public void A_non_string_slot_value_is_ignored() =>
        Assert.Empty(ThemeSlots.ColoursFrom("""{"accent":16711680,"ink":true,"line":null}"""));

    // ── base mode ─────────────────────────────────────────────────────────────────────────────

    /// <summary>⚠ Only an exact light/dark forces a scheme. Everything else follows the device, which is
    /// `theme.ts`'s `color-scheme: light dark` and what an unthemed till already does.</summary>
    [Theory]
    [InlineData("light", ThemeBaseMode.Light)]
    [InlineData("dark", ThemeBaseMode.Dark)]
    [InlineData("LIGHT", ThemeBaseMode.Light)]
    [InlineData(" Dark ", ThemeBaseMode.Dark)]
    [InlineData("system", ThemeBaseMode.System)]
    [InlineData("auto", ThemeBaseMode.System)]      // not a value either till knows
    [InlineData("", ThemeBaseMode.System)]
    [InlineData(null, ThemeBaseMode.System)]
    public void The_base_mode_follows_the_device_unless_told_otherwise(string? mode, ThemeBaseMode expected) =>
        Assert.Equal(expected, ThemeSlots.ModeFrom(mode));

    // ── what the operator is shown ────────────────────────────────────────────────────────────

    /// <summary>⚠ Identical wording to `theme.ts currentThemeLabel` for the no-theme case. An operator
    /// comparing two tills is usually doing it because something already looks wrong.</summary>
    [Fact]
    public void No_theme_is_described_the_way_the_web_till_describes_it() =>
        Assert.Equal("Plutus (light & dark follow this device)", ThemeSlots.Describe(null, null, "default"));

    [Fact]
    public void An_assigned_theme_names_itself_and_where_it_came_from()
    {
        var text = ThemeSlots.Describe("plutus-dark", "Plutus Dark", "store");

        Assert.Contains("Plutus Dark", text);
        Assert.Contains("store", text);
    }

    /// <summary>⚠ A theme with a key but no name still reads as something — "Custom", as the web till
    /// does. A blank label looks like a fault.</summary>
    [Fact]
    public void A_nameless_theme_is_called_Custom() =>
        Assert.Contains("Custom", ThemeSlots.Describe("some-key", null, "till"));
}
