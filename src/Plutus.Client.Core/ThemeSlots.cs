using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Plutus.Client.Core;

/// <summary>Which light/dark footing a theme sits on. ⚠ `System` means "follow the device".</summary>
public enum ThemeBaseMode
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>
/// The seven colour slots a portal theme can set, and the rule for reading them (FE10 / step 22).
///
/// ⚠⚠ THIS IS A C2 TWIN, AND IT IS THE WHOLE POINT OF THE CLASS. Matt, 2026-08-17: *"Changing the
/// theme in the portal needs to be consistent across the web and MAUI till."* The web till's half is
/// `theme.ts` — TypeScript, so the code cannot literally be shared — and every rule below is
/// deliberately identical to it, slot for slot. Two tills given the same assignment must show the same
/// colours, or the portal's preview is a lie about at least one of them.
///
/// ⚠ `colorsJson` is an **opaque blob owned by the frontends** (see `EffectiveThemeResult`). The
/// server does not validate it, which makes this class the **only** thing standing between a bad
/// value in that blob and an unreadable till.
///
/// The rules, each mirroring `theme.ts`:
///
/// | | |
/// |---|---|
/// | **Seven slots, these names** | `accent`, `accentInk`, `surface`, `surface2`, `ink`, `inkMuted`, `line` |
/// | **Six-digit hex only** | `^#[0-9a-fA-F]{6}$` — the web till calls this *"the till's last line of defence against a bad value in the blob"* |
/// | **A bad slot is ABSENT, never a substitute** | The web till `removeProperty`s it so the stylesheet's own default shows. MAUI must leave the stock palette key alone for the same reason — a guessed colour is how you get white-on-white |
/// | ⚠⚠ **A bad blob still applies `baseMode`** | *"opaque blob was bad — base mode still applies"*. Light/dark is useful on its own and must not be lost to a malformed colour |
/// | **An unknown slot name is ignored** | A later portal may send an eighth; an older till must not choke on it |
/// </summary>
public static class ThemeSlots
{
    /// <summary>⚠ The stock accent — `#2c698d`, which is the MAUI palette's `Quinary` **and** the web
    /// till's `DEFAULT_ACCENT`. The two palettes were designed together; keeping this one literal in
    /// step with both is what makes "clear every override" land in the same place on each till.</summary>
    public const string DefaultAccent = "#2c698d";

    /// <summary>The slot names, in the order the portal and both tills list them.</summary>
    public static readonly string[] Names =
        { "accent", "accentInk", "surface", "surface2", "ink", "inkMuted", "line" };

    // ⚠ Six digits only. `#abc` shorthand and 8-digit `#rrggbbaa` are both REFUSED — the web till
    // refuses them, and a till that accepted an alpha channel could render text at 0% opacity.
    private static readonly Regex Hex = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    /// <summary>⚠ Is this a colour a till may apply? The one gate.</summary>
    public static bool IsUsableColour(string? value) =>
        value is not null && Hex.IsMatch(value.Trim());

    /// <summary>
    /// Parse `baseMode`. ⚠ Anything unrecognised — including null — is <see cref="ThemeBaseMode.System"/>,
    /// matching `theme.ts`, where only an exact "light"/"dark" forces a scheme and everything else
    /// leaves `color-scheme: light dark`. Following the device is the safe answer: it is what an
    /// unthemed till already does.
    /// </summary>
    public static ThemeBaseMode ModeFrom(string? baseMode) =>
        (baseMode?.Trim().ToLowerInvariant()) switch
        {
            "light" => ThemeBaseMode.Light,
            "dark" => ThemeBaseMode.Dark,
            _ => ThemeBaseMode.System,
        };

    /// <summary>
    /// The usable colours in <paramref name="colorsJson"/>, keyed by slot name.
    ///
    /// ⚠⚠ ONLY VALID SLOTS APPEAR. A caller applies what is here and **leaves every other slot at its
    /// stock value** — it must not substitute, interpolate or default a missing one. That is what makes
    /// "clear the override in the portal" restore the built-in palette exactly, which in turn is what
    /// makes the stock palette a real fallback rather than dead code.
    ///
    /// ⚠ NEVER THROWS. A malformed blob returns empty, and the caller still applies the base mode.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ColoursFrom(string? colorsJson)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(colorsJson)) return found;

        try
        {
            using var doc = JsonDocument.Parse(colorsJson);

            if (doc.RootElement.ValueKind != JsonValueKind.Object) return found;

            foreach (var slot in Names)
            {
                // ⚠ EXACT, CASE-SENSITIVE slot names — the blob is written by the portal against the
                // web till's `ThemeColors` interface, whose keys are camelCase. Matching loosely here
                // would accept `Accent` on one till and not the other.
                if (!doc.RootElement.TryGetProperty(slot, out var value)) continue;
                if (value.ValueKind != JsonValueKind.String) continue;

                var colour = value.GetString();
                if (IsUsableColour(colour)) found[slot] = colour!.Trim();
            }
        }
        catch (JsonException)
        {
            // ⚠ Deliberately swallowed — see the header. Base mode survives a bad blob.
        }

        return found;
    }

    /// <summary>
    /// The ink that can actually be READ on this background — `#000000` or `#ffffff`.
    ///
    /// ⚠⚠ WCAG relative luminance, and the 0.179 threshold is not a guess: it is the point where black
    /// and white contrast EQUALLY against a background, so either side of it the answer is the one with
    /// more contrast. Picking by "is the hex big" instead gets mid-greens wrong, and a mid-green accent
    /// is exactly what a shop with a brand colour will set.
    ///
    /// ⚠ Only ever black or white, deliberately. Interpolating a "nearly readable" ink is how you get
    /// 2.94:1 — legible enough to ship and illegible under a shop's lights, which is the class of fault
    /// this whole work package exists to close.
    ///
    /// ⚠ Returns null for anything that is not a six-digit hex, so a caller falls back to the stock
    /// value rather than applying a colour derived from rubbish.
    /// </summary>
    public static string? ReadableInkOn(string? background)
    {
        if (!IsUsableColour(background)) return null;

        var hex = background!.Trim().Substring(1);

        static double Channel(int v)
        {
            var s = v / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        var r = Channel(Convert.ToInt32(hex.Substring(0, 2), 16));
        var g = Channel(Convert.ToInt32(hex.Substring(2, 2), 16));
        var b = Channel(Convert.ToInt32(hex.Substring(4, 2), 16));

        var luminance = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);

        return luminance > 0.179 ? "#000000" : "#ffffff";
    }

    /// <summary>
    /// Fill in the second half of any slot PAIR the portal only half-set — WP-T1 T1.2, 2026-08-19.
    ///
    /// ⚠⚠ THE FAULT THIS CLOSES IS THE DEFAULT CONFIGURATION, NOT AN EDGE CASE. A theme of
    /// `{"accent":"#f5f5c0"}` — one pale colour, which is exactly what a shop picking a brand colour
    /// sets — leaves `accentInk` at its stock WHITE, so every accent button on both tills renders white
    /// text on a pale yellow ground. `Theming.cs` claimed a malformed blob "cannot produce
    /// white-on-white"; that is true of malformed ones and **not** of a well-formed partial one.
    ///
    /// ⚠⚠ **SEPARATE FROM <see cref="ColoursFrom"/> ON PURPOSE.** That method's contract is *"only valid
    /// slots appear… it must not substitute, interpolate or default a missing one"*, and that contract is
    /// what makes "clear the override in the portal" restore the stock palette EXACTLY. Deriving inside
    /// it would have quietly broken that. So the parse stays literal and the derivation is a second,
    /// explicit step a caller opts into.
    ///
    /// ⚠ TWO PAIRS, NAMED, AND NO OTHERS. `accentInk` follows `accent`; `ink` follows `surface`. Nothing
    /// else is inferred — inventing a `surface2` from a `surface` is a design decision this code has no
    /// business making, and a stock one is a colour somebody chose.
    ///
    /// ⚠ A slot the portal DID set is never touched, even when it contrasts badly. An owner who
    /// deliberately set both halves owns the result; this only fills silence.
    ///
    /// ⚠⚠ C2 TWIN of `theme.ts`'s `withDerivedPairs`. Two tills that fill the gap differently show one
    /// shop two different screens from one theme.
    /// </summary>
    public static IReadOnlyDictionary<string, string> WithDerivedPairs(
        IReadOnlyDictionary<string, string>? applied)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (applied is null) return result;

        foreach (var (slot, colour) in applied) result[slot] = colour;

        Pair("accent", "accentInk");
        Pair("surface", "ink");

        return result;

        void Pair(string background, string ink)
        {
            if (!result.ContainsKey(background) || result.ContainsKey(ink)) return;
            if (ReadableInkOn(result[background]) is string derived) result[ink] = derived;
        }
    }

    /// <summary>
    /// What Settings shows the operator — *"Plutus Dark — set for this store in the portal"*.
    ///
    /// ⚠ Mirrors `theme.ts currentThemeLabel`, including the no-theme wording, so the two tills describe
    /// the same assignment the same way. An operator comparing them is usually doing so because
    /// something already looks wrong.
    /// </summary>
    public static string Describe(string? themeKey, string? name, string? source) =>
        string.IsNullOrWhiteSpace(themeKey)
            ? "Plutus (light & dark follow this device)"
            : $"{(string.IsNullOrWhiteSpace(name) ? "Custom" : name)} — set for this "
              + $"{(string.IsNullOrWhiteSpace(source) ? "estate" : source)} in the portal";
}
