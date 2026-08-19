using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Plutus.Frontend.AppClient.Helpers.Extensions.XAML;

namespace Plutus.Frontend.AppClient.Helpers.Compatibility
{
    /// <summary>
    /// Drop-in replacement for Plugin.Iconize's IconToolbarItem (no .NET MAUI package exists),
    /// letting call sites keep assigning a "md-xxx" glyph key string to IconImageSource and a
    /// separate IconColor, instead of building a FontImageSource by hand.
    /// </summary>
    public class IconToolbarItem : ToolbarItem
    {
        private static readonly MaterialIconGlyphConverter GlyphConverter = new MaterialIconGlyphConverter();

        private string _iconKey;
        // ⚠ Themed, not black (WP-T1 T1.1): these sit on the accent chrome, and a hardcoded black
        // icon on a dark accent is the same invisible-control fault as DialogHeader's ✕ was.
        private Color _iconColor =
            Extensions.XAML.MaterialIconGlyphConverter.ThemeColour("ThemeAccentInk", Colors.Black);

        public new string IconImageSource
        {
            get => _iconKey;
            set
            {
                _iconKey = value;
                ApplyIcon();
            }
        }

        public Color IconColor
        {
            get => _iconColor;
            set
            {
                _iconColor = value;
                ApplyIcon();
            }
        }

        public bool IsVisible { get; set; } = true;

        private void ApplyIcon()
        {
            base.IconImageSource = (ImageSource)GlyphConverter.Convert(_iconKey, typeof(ImageSource), _iconColor, null);
        }
    }
}
