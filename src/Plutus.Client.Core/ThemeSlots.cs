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
