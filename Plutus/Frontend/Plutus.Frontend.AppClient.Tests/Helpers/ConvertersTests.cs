using System.Globalization;
using Plutus.Frontend.AppClient.Helpers.Extensions.XAML;

namespace Plutus.Frontend.AppClient.Tests.Helpers
{
    public class BoolANDGateConverterTests
    {
        private readonly BoolANDGateConverter _converter = new();

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, false)]
        public void Convert_AndsValueAndParameter(bool value, bool parameter, bool expected)
        {
            Assert.Equal(expected, _converter.Convert(value, typeof(bool), parameter, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void Convert_NonBooleanInputs_Throws()
        {
            Assert.Throws<ArgumentException>(() => _converter.Convert("not-a-bool", typeof(bool), true, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void ConvertBack_Throws()
        {
            Assert.Throws<NotSupportedException>(() => _converter.ConvertBack(true, typeof(bool), true, CultureInfo.InvariantCulture));
        }
    }

    public class CollectionEmptyBoolConverterTests
    {
        private readonly CollectionEmptyBoolConverter _converter = new();

        [Fact]
        public void Convert_EmptyCollection_ReturnsFalse()
        {
            Assert.Equal(false, _converter.Convert(new List<int>(), typeof(bool), null!, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void Convert_NonEmptyCollection_ReturnsTrue()
        {
            Assert.Equal(true, _converter.Convert(new List<int> { 1 }, typeof(bool), null!, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void Convert_NonCollection_Throws()
        {
            Assert.Throws<ArgumentException>(() => _converter.Convert(42, typeof(bool), null!, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void ConvertBack_Throws()
        {
            Assert.Throws<NotSupportedException>(() => _converter.ConvertBack(true, typeof(object), null!, CultureInfo.InvariantCulture));
        }
    }

    public class InverseBoolConverterTests
    {
        private readonly InverseBoolConverter _converter = new();

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void Convert_InvertsBoolean(bool value, bool expected)
        {
            Assert.Equal(expected, _converter.Convert(value, typeof(bool), null!, CultureInfo.InvariantCulture));
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void ConvertBack_InvertsBoolean(bool value, bool expected)
        {
            Assert.Equal(expected, _converter.ConvertBack(value, typeof(bool), null!, CultureInfo.InvariantCulture));
        }
    }

    public class PickerIndexToDBIdConverterTests
    {
        private readonly PickerIndexToDBIdConverter _converter = new();

        [Fact]
        public void Convert_AddsOne()
        {
            Assert.Equal(3, _converter.Convert(2, typeof(int), null!, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void ConvertBack_SubtractsOne()
        {
            Assert.Equal(2, _converter.ConvertBack(3, typeof(int), null!, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void Convert_NonInteger_Throws()
        {
            Assert.Throws<NotSupportedException>(() => _converter.Convert("2", typeof(int), null!, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void ConvertBack_NonInteger_Throws()
        {
            Assert.Throws<NotSupportedException>(() => _converter.ConvertBack("2", typeof(int), null!, CultureInfo.InvariantCulture));
        }
    }

    public class StringNullEmptyBoolConverterTests
    {
        private readonly StringNullEmptyBoolConverter _converter = new();

        [Theory]
        [InlineData("text", true)]
        [InlineData("", false)]
        public void Convert_ReturnsWhetherStringHasContent(string value, bool expected)
        {
            Assert.Equal(expected, _converter.Convert(value, typeof(bool), null!, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void Convert_NonString_Throws()
        {
            Assert.Throws<ArgumentException>(() => _converter.Convert(42, typeof(bool), null!, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void ConvertBack_Throws()
        {
            Assert.Throws<NotSupportedException>(() => _converter.ConvertBack(true, typeof(string), null!, CultureInfo.InvariantCulture));
        }
    }

    public class MaterialIconGlyphConverterTests
    {
        private readonly MaterialIconGlyphConverter _converter = new();

        [Fact(Skip = "FontImageSource derives from Microsoft.Maui.Controls.Element (BindableObject), " +
            "whose constructor requires a live WinUI3 dispatcher unavailable in a plain xUnit process " +
            "- see ValidationGroupBehaviorTests for the same underlying constraint.")]
        public void Convert_KnownKey_ReturnsFontImageSourceWithGlyph()
        {
            var result = _converter.Convert("md-store", typeof(object), null!, CultureInfo.InvariantCulture);
            var fontImageSource = Assert.IsType<Microsoft.Maui.Controls.FontImageSource>(result);
            Assert.Equal("MaterialIconsRegular", fontImageSource.FontFamily);
            Assert.Equal(char.ConvertFromUtf32(0xE8D1), fontImageSource.Glyph);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("md-does-not-exist")]
        public void Convert_UnknownOrEmptyKey_ReturnsNull(string? key)
        {
            Assert.Null(_converter.Convert(key!, typeof(object), null!, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void ConvertBack_Throws()
        {
            Assert.Throws<NotSupportedException>(() => _converter.ConvertBack(null!, typeof(string), null!, CultureInfo.InvariantCulture));
        }
    }
}
