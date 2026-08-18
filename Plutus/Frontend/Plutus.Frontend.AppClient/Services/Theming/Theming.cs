using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Plutus.Client.Core;
using Plutus.Client.Storage;
using Plutus.Contracts.Client;

namespace Plutus.Frontend.AppClient.Services.Theming
{
    /// <summary>
    /// The portal decides this till's colours; the till wears them (FE10 / step 22, 7b).
    ///
    /// ⚠⚠ MATT, 2026-08-17: *"Changing the theme in the portal needs to be consistent across the web and
    /// MAUI till."* So **nothing here decides anything about colour.** The server resolves
    /// till > group > store > tenant > default in `ThemeResolution`; `Client.Core.ThemeSlots` — the C2
    /// twin of the web till's `theme.ts` — decides which values are usable. This class only fetches,
    /// caches, and writes the seven slots into `Application.Resources`.
    ///
    /// ⚠ A TILL DOES NOT CHOOSE ITS COLOURS. There is deliberately no picker: an operator changing the
    /// scheme locally would make the portal's assignment a suggestion, and two tills in one shop would
    /// stop matching for reasons nobody could see from the portal.
    ///
    /// ⚠⚠ ONLY THE SEVEN `Theme*` KEYS ARE TOUCHED. A slot the portal did not send, or sent badly, is
    /// **left at its stock value** — never substituted or interpolated. That is what makes clearing an
    /// override in the portal restore the built-in palette exactly, and it is the reason a malformed
    /// blob cannot produce white-on-white.
    ///
    /// ⚠ RECEIPTS ARE IMMUNE (till-design C1). Nothing on a print path reads these keys, and it must
    /// stay that way: printing from a dark scheme once put near-white ink on paper.
    /// </summary>
    internal static class Theming
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        /// <summary>Maps a wire slot name to the resource key the XAML binds. ⚠ Same seven slots as
        /// `theme.ts SLOT_VARS`, in the same order — see `ThemeSlots.Names`.</summary>
        private static string KeyFor(string slot) => slot switch
        {
            "accent" => "ThemeAccent",
            "accentInk" => "ThemeAccentInk",
            "surface" => "ThemeSurface",
            "surface2" => "ThemeSurface2",
            "ink" => "ThemeInk",
            "inkMuted" => "ThemeInkMuted",
            "line" => "ThemeLine",
            _ => "",
        };

        /// <summary>The stock value of each slot, captured before anything overwrites it. ⚠ These are
        /// the LIGHT palette — `Colors.xaml` defines one set, and it is the light one.</summary>
        private static readonly System.Collections.Generic.Dictionary<string, Color> Stock = new();

        /// <summary>
        /// The stock palette's **dark half**, which `Colors.xaml` does not have.
        ///
        /// ⚠⚠ WITHOUT THIS, A DARK SCHEME THAT SETS ONLY SOME SLOTS PRODUCES A LIGHT APP IN DARK MODE.
        /// The live "Kapow Test" theme is exactly that shape — `baseMode: dark` with
        /// `{"accent":…,"line":…}` and nothing else — so `UserAppTheme` went Dark while `ThemeSurface`
        /// fell back to the stock **#ffffff**: white pages, dark platform chrome, and a scheme that
        /// "does not look like it worked". A partial scheme is the normal case, not an edge one; the
        /// portal lets you set one slot.
        ///
        /// ⚠ IT IS A FALLBACK, NEVER AN OVERRIDE. A slot the scheme DOES set still wins, in either
        /// mode — this only answers "what should the slots it left alone be, given the mode it asked
        /// for?"
        ///
        /// ⚠ INK AND SURFACE AS A PAIR, the same rule the styles follow: swapping the surface without
        /// swapping the ink is the unreadable-label fault (1.74.0) with the lights off.
        ///
        /// ⚠ The accent keeps its stock hue in both modes — it is the brand, and a shop's colour does
        /// not change because the room is dark. Only its INK flips, so text on it stays legible.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<string, Color> DarkStock = new()
        {
            ["ThemeAccent"] = Color.FromArgb("#2c698d"),
            ["ThemeAccentInk"] = Color.FromArgb("#ffffff"),
            ["ThemeSurface"] = Color.FromArgb("#161d26"),
            ["ThemeSurface2"] = Color.FromArgb("#212b38"),
            ["ThemeInk"] = Color.FromArgb("#eef2f6"),
            ["ThemeInkMuted"] = Color.FromArgb("#9aa7b4"),
            ["ThemeLine"] = Color.FromArgb("#33404f"),
        };

        /// <summary>
        /// Apply a theme to the running app. ⚠ MUST be called on the UI thread.
        ///
        /// ⚠⚠ THE STOCK VALUES ARE CAPTURED ON FIRST USE and restored for any slot the theme does not
        /// set. Without that, applying theme A then theme B would leave A's colours in every slot B
        /// omits — so "clear the override" would restore whatever happened to be applied last rather
        /// than the palette, and the fallback would be a lie.
        /// </summary>
        public static void Apply(EffectiveThemeResult theme)
        {
            var resources = Application.Current?.Resources;
            if (resources is null) return;

            // Base mode first: it is useful on its own, and `ThemeSlots.ColoursFrom` returns empty for
            // a malformed blob precisely so this still happens.
            var mode = ThemeSlots.ModeFrom(theme?.BaseMode);

            Application.Current!.UserAppTheme = mode switch
            {
                ThemeBaseMode.Light => AppTheme.Light,
                ThemeBaseMode.Dark => AppTheme.Dark,
                _ => AppTheme.Unspecified,   // ⚠ = follow the device, the web till's "light dark"
            };

            // ⚠⚠ WHICH STOCK PALETTE THE UNSET SLOTS FALL BACK TO — see `DarkStock`. A scheme that asks
            // for dark and sets only an accent must not leave the surfaces white; that is the live
            // theme's exact shape, and it is why a dark scheme looked like it had done nothing.
            //
            // ⚠ `Unspecified` follows the DEVICE, and the device's mode is not knowable here — so it
            // keeps the light stock, which is what the app has always shipped. Guessing dark from a
            // device setting the theme did not mention would be inventing a decision nobody made.
            var darkFallback = mode == ThemeBaseMode.Dark;

            var colours = ThemeSlots.ColoursFrom(theme?.ColorsJson);

            foreach (var slot in ThemeSlots.Names)
            {
                var key = KeyFor(slot);
                if (key.Length == 0) continue;

                if (!Stock.ContainsKey(key) && resources.TryGetValue(key, out var existing) && existing is Color c)
                    Stock[key] = c;

                if (colours.TryGetValue(slot, out var hex))
                {
                    // ⚠ THE SCHEME ALWAYS WINS, in either mode. `DarkStock` answers only for the slots
                    // it did not set.
                    resources[key] = Color.FromArgb(hex);
                }
                else if (darkFallback && DarkStock.TryGetValue(key, out var dark))
                {
                    resources[key] = dark;
                }
                else if (Stock.TryGetValue(key, out var original))
                {
                    // ⚠ RESTORE, do not leave the previous theme's value behind — see the header.
                    resources[key] = original;
                }

                // ⚠ The matching brush is rebuilt too. A `SolidColorBrush` created from a
                // `StaticResource` captured the colour at parse time and does NOT follow a later
                // change — so a control bound to `ThemeAccentBrush` would keep the stock colour while
                // one bound to `ThemeAccent` changed. Half a themed screen is worse than none.
                if (resources[key] is Color applied)
                    resources[key + "Brush"] = new SolidColorBrush(applied);
            }
        }

        /// <summary>
        /// Apply whatever is cached, without touching the network. ⚠ Called at start-up **before** the
        /// first screen, so the till does not flash the stock palette — the same reason the web till
        /// applies its cache in `main.tsx` before React mounts.
        /// </summary>
        public static async Task ApplyCachedAsync(CancellationToken ct = default)
        {
            try
            {
                if (await CachedAsync(ct).ConfigureAwait(false) is EffectiveThemeResult cached)
                    MainThread.BeginInvokeOnMainThread(() => Apply(cached));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Analytics.CrashLog.Write("Theming.ApplyCached", ex);
            }
        }

        /// <summary>
        /// Fetch the effective theme and apply it. Called on the 60s cadence. Never throws.
        ///
        /// ⚠ A FAILED FETCH KEEPS THE LAST-KNOWN THEME — colours must never depend on the network being
        /// up, and reverting a shop's branding mid-day because a poll failed would be a visible fault
        /// caused by an invisible one. Same contract as the web till's `refreshTheme`.
        /// </summary>
        public static async Task RefreshAsync(CancellationToken ct = default)
        {
            try
            {
                var api = await Storage.TillPlacement.TryCreateApiAsync(ct).ConfigureAwait(false);
                if (api is null) return;

                // ⚠ TILL FIRST, STORE AS FALLBACK — the same query the web till builds. The server does
                // the resolving; sending the till id is what lets a till-level override beat the store.
                var tillId = await Storage.TillPlacement.TillIdAsync(api, ct).ConfigureAwait(false);
                var storeId = tillId is null
                    ? await Storage.TillPlacement.StoreIdAsync(ct: ct).ConfigureAwait(false)
                    : null;

                if (tillId is null && storeId is null) return;

                var theme = await api.GetEffectiveThemeAsync(tillId, storeId, ct).ConfigureAwait(false);
                if (theme is null) return;

                await Storage.TillStoreAccess.TryUseAsync(async s =>
                {
                    await s.SetMetaAsync(MetaKeys.EffectiveTheme, JsonSerializer.Serialize(theme, Json), ct)
                        .ConfigureAwait(false);
                    return true;
                }, ct).ConfigureAwait(false);

                MainThread.BeginInvokeOnMainThread(() => Apply(theme));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // ⚠ Colours are never worth disturbing a till for.
                Analytics.CrashLog.Write("Theming.Refresh", ex);
            }
        }

        /// <summary>What Settings shows. ⚠ Wording comes from `ThemeSlots.Describe`, shared with the
        /// web till, so both tills describe the same assignment identically.</summary>
        public static async Task<string> DescribeAsync(CancellationToken ct = default)
        {
            var cached = await CachedAsync(ct).ConfigureAwait(false);
            return ThemeSlots.Describe(cached?.ThemeKey, cached?.Name, cached?.Source);
        }

        private static async Task<EffectiveThemeResult> CachedAsync(CancellationToken ct)
        {
            var raw = await Storage.TillStoreAccess.TryUseAsync(
                s => s.GetMetaAsync(MetaKeys.EffectiveTheme, ct), ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(raw)) return null;

            try { return JsonSerializer.Deserialize<EffectiveThemeResult>(raw, Json); }
            catch (JsonException) { return null; }   // ⚠ A corrupt cache is "no theme", not a crash
        }
    }
}
