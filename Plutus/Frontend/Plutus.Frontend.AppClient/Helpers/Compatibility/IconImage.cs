using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Plutus.Frontend.AppClient.Helpers.Extensions.XAML;

namespace Plutus.Frontend.AppClient.Helpers.Compatibility
{
    /// <summary>
    /// Drop-in replacement for Plugin.Iconize's IconImage (no .NET MAUI package exists), letting
    /// call sites keep assigning a "md-xxx" glyph key string plus a color/size, instead of building
    /// a FontImageSource by hand.
    /// </summary>
    public class IconImage : Image
    {
        private static readonly MaterialIconGlyphConverter GlyphConverter = new MaterialIconGlyphConverter();

        private string _iconKey;
        private Color _iconColor = Colors.Black;
        private double _iconSize = 24;

        public string Icon
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

        public double IconSize
        {
            get => _iconSize;
            set
            {
                _iconSize = value;
                ApplyIcon();
            }
        }

        private void ApplyIcon()
        {
            if (GlyphConverter.Convert(_iconKey, typeof(ImageSource), _iconColor, null) is FontImageSource fontImageSource)
            {
                fontImageSource.Size = _iconSize;
                Source = fontImageSource;
            }
        }
    }
}
