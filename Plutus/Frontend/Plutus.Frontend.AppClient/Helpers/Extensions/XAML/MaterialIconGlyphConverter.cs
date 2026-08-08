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

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string key || string.IsNullOrEmpty(key) || !Codepoints.TryGetValue(key, out var codepoint))
                return null;

            return new FontImageSource
            {
                FontFamily = "MaterialIconsRegular",
                Glyph = char.ConvertFromUtf32(codepoint),
                Color = parameter as Color ?? Colors.Black,
                Size = 24,
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
