using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Helpers.Extensions.XAML
{
    /// <summary>
    /// Replaces Plugin.Iconize's global "md-xxx"/"fa-xxx" string-to-icon resolution (which has no
    /// .NET MAUI equivalent) with an explicit converter from the same glyph-name strings the
    /// ViewModels already expose via their <c>Icon</c> property, to a FontImageSource using the
    /// real Material Icons font. Only the handful of glyphs this app actually references are mapped;
    /// see Resources/Fonts/materialicons.ttf (registered as "MaterialIconsRegular" in MauiProgram.cs).
    /// Codepoints are from Google's canonical MaterialIcons-Regular.codepoints file.
    /// </summary>
    public class MaterialIconGlyphConverter : IValueConverter
    {
        private static readonly Dictionary<string, int> Codepoints = new Dictionary<string, int>
        {
            ["md-store"] = 0xE8D1,
            ["md-shopping-basket"] = 0xE8CB,
            ["md-data-usage"] = 0xE1AF,
            ["md-settings"] = 0xE8B8,
            ["md-check-circle"] = 0xE86C,
            ["md-all-inbox"] = 0xE97F,
            ["md-people"] = 0xE7FB,
            ["md-table-chart"] = 0xE265,
            // MAUI retrofit: the Plutus (platform connection) tab.
            ["md-cloud"] = 0xE2BD,
        };

        /// <summary>
        /// One themed colour, by key, or <paramref name="fallback"/> when it cannot be had.
        ///
        /// ⚠ NEVER THROWS. This runs inside a value converter, and an exception there is a control that
        /// silently renders nothing — the failure mode this whole work package exists to close.
        /// </summary>
        internal static Color ThemeColour(string key, Color fallback)
        {
            try
            {
                return Application.Current?.Resources?.TryGetValue(key, out var value) == true && value is Color c
                    ? c
                    : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string key || string.IsNullOrEmpty(key) || !Codepoints.TryGetValue(key, out var codepoint))
                return null;

            return new FontImageSource
            {
                FontFamily = "MaterialIconsRegular",
                Glyph = char.ConvertFromUtf32(codepoint),
                // ⚠⚠ RESOLVED FROM THE THEME AT CONVERT TIME — WP-T1 T1.1, 2026-08-19. This defaulted to
                // `Colors.Black` while two toolbar items passed `Colors.White`, so the SAME tab bar
                // carried icons hardcoded in OPPOSITE directions and at most one of them could be right.
                // The bar now has an accent ground (`Styles.xaml`'s `Shell` style), so its icons take
                // `ThemeAccentInk` — the pairing T1.2 guarantees is legible.
                //
                // ⚠ AT CONVERT TIME, because a converter cannot `SetDynamicResource`. A theme change
                // therefore does not recolour an icon already built — accepted: the alternative is
                // rebuilding every `FontImageSource` on a theme change, and the bar is redrawn on the
                // next navigation anyway.
                Color = parameter as Color ?? ThemeColour("ThemeAccentInk", Colors.Black),
                Size = 24,
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
