using I18N_L10N;
using I18N_L10N.Extensions;

namespace Plutus.Frontend.AppClient.Tests.I18N_L10N
{
    public class PlatformCultureTests
    {
        [Fact]
        public void Construction_WithUnderscore_NormalizesToHyphen()
        {
            var culture = new PlatformCulture("en_GB");
            Assert.Equal("en-GB", culture.PlatformString);
            Assert.Equal("en", culture.LanguageCode);
            Assert.Equal("GB", culture.LocaleCode);
        }

        [Fact]
        public void Construction_WithHyphen_ParsesLanguageAndLocale()
        {
            var culture = new PlatformCulture("fr-FR");
            Assert.Equal("fr", culture.LanguageCode);
            Assert.Equal("FR", culture.LocaleCode);
        }

        [Fact]
        public void Construction_LanguageOnly_HasEmptyLocaleCode()
        {
            var culture = new PlatformCulture("en");
            Assert.Equal("en", culture.LanguageCode);
            Assert.Equal("", culture.LocaleCode);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Construction_NullOrEmpty_Throws(string? value)
        {
            Assert.Throws<ArgumentException>(() => new PlatformCulture(value!));
        }

        [Fact]
        public void ToString_ReturnsPlatformString()
        {
            Assert.Equal("en-GB", new PlatformCulture("en_GB").ToString());
        }
    }

    public class I18N_L10NTests
    {
        [Fact]
        public void SetCulture_OnWindows_IsANoOp()
        {
            // I18N_L10N.SetCulture() only resolves ILocalize (an iOS/Android-only DependencyService
            // implementation) when DeviceInfo.Platform is iOS or Android; on Windows it's a no-op, so
            // this should never throw even without any ILocalize registered.
            var ex = Record.Exception(() => global::I18N_L10N.I18N_L10N.SetCulture());
            Assert.Null(ex);
        }
    }

    public class TranslateExtensionTests
    {
        [Fact]
        public void KeyConstructor_ResolvesTranslationImmediately()
        {
            var extension = new TranslateExtension("Login");
            Assert.Equal("Login", extension.Text);
        }

        [Fact]
        public void KeyConstructor_UnknownKey_Throws()
        {
            Assert.Throws<ArgumentException>(() => new TranslateExtension("DefinitelyNotARealTranslationKey12345"));
        }

        [Fact]
        public void ProvideValue_IServiceProvider_UsesTextProperty()
        {
            var extension = new TranslateExtension { Text = "Login" };
            var result = extension.ProvideValue((IServiceProvider)null!);
            Assert.NotNull(result);
        }

        [Fact]
        public void ProvideValue_IServiceProvider_WithNullText_ReturnsEmptyString()
        {
            var extension = new TranslateExtension();
            Assert.Equal("", extension.ProvideValue((IServiceProvider)null!));
        }
    }
}
