using Microsoft.Maui.Controls;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Helpers.Validators;

namespace Plutus.Frontend.AppClient.Tests.Helpers
{
    public class ValidatorsTests
    {
        [Theory]
        [InlineData("hello", true)]
        [InlineData(" ", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void RequiredValidator_Check(string? value, bool expected)
        {
            Assert.Equal(expected, new RequiredValidator().Check(value!));
        }

        [Fact]
        public void RequiredValidator_HasMessage()
        {
            Assert.False(string.IsNullOrEmpty(new RequiredValidator().Message));
        }

        [Theory]
        [InlineData("test@example.com", true)]
        [InlineData("first.last@sub.example.co.uk", true)]
        [InlineData("not-an-email", false)]
        [InlineData("missing@domain", false)]
        [InlineData("@example.com", false)]
        public void EmailValidator_Check(string value, bool expected)
        {
            Assert.Equal(expected, new EmailValidator().Check(value));
        }

        [Theory]
        [InlineData("10.50", true)]
        [InlineData("£10.50", true)]
        [InlineData("1,000.50", true)]
        [InlineData("not-a-number", false)]
        public void CurrencyValueValidator_Check_DefaultStyle(string value, bool expected)
        {
            using var _ = new CultureScope("en-GB");
            Assert.Equal(expected, new CurrencyValueValidator().Check(value));
        }

        [Fact]
        public void CurrencyValueValidator_Check_CustomStyles_CombinesFlags()
        {
            var validator = new CurrencyValueValidator(System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.NumberStyles.AllowLeadingSign);
            Assert.True(validator.Check("-1.5"));
        }

        [Theory]
        [InlineData("1.23", true)]
        [InlineData("42", true)]
        [InlineData("abc", false)]
        public void DecimalValidator_Check(string value, bool expected)
        {
            Assert.Equal(expected, new DecimalValidator().Check(value));
        }

        [Theory]
        [InlineData("42", true)]
        [InlineData("-5", true)]
        [InlineData("1.5", false)]
        [InlineData("abc", false)]
        public void IntegerValidator_Check(string value, bool expected)
        {
            Assert.Equal(expected, new IntegerValidator().Check(value));
        }

        [Theory]
        [InlineData("", true)]
        [InlineData("07911123456", true)]
        [InlineData("+447911123456", true)]
        [InlineData("not-a-number", false)]
        public void MobileNumValidator_Check(string value, bool expected)
        {
            Assert.Equal(expected, new MobileNumValidator().Check(value));
        }

        [Theory]
        [InlineData("", true)]
        // BUG: NiNoValidator's regex uses "[A-Z&&[^DFIQUV]]", which is not valid .NET character-class
        // subtraction syntax (that's "[A-Z-[DFIQUV]]"). As written, the character class closes at the
        // first unescaped ']' and leaves a stray literal ']' in the pattern, so no realistic NINO
        // (including genuinely valid ones) can ever match. Documenting actual behavior, not desired
        // behavior - see the discovered-bug note in the test project README.
        [InlineData("AB123456C", false)]
        [InlineData("BG123456C", false)]
        [InlineData("123456", false)]
        public void NiNoValidator_Check(string value, bool expected)
        {
            Assert.Equal(expected, new NiNoValidator().Check(value));
        }

        [Theory]
        [InlineData("Passw0rd!", true)]
        [InlineData("password", false)]
        [InlineData("PASSWORD1!", false)]
        [InlineData("Short1!", false)]
        public void PasswordValidator_Check(string value, bool expected)
        {
            Assert.Equal(expected, new PasswordValidator().Check(value));
        }

        [Fact]
        public void PasswordConfValidator_Construction_SetsDefaultTranslatedMessage()
        {
            // Check() itself needs a real ReferenceEntry (a Microsoft.Maui.Controls.Entry), which can't
            // be constructed here - see the Skip'd test below - but the default Message value is plain
            // string formatting reachable without one.
            var validator = new PasswordConfValidator();
            Assert.Equal(string.Format("NotIdenticle".Translate(), "Password".Translate()), validator.Message);
        }

        [Fact(Skip = "Constructing any Microsoft.Maui.Controls type (Entry, Label, Page, ...) in a plain " +
            "console/xUnit test process throws inside Microsoft.Maui.Handlers.ViewHandler's static " +
            "constructor (Microsoft.UI.Xaml.Input.FocusManager.add_GotFocus requires a live WinUI3 " +
            "dispatcher/UI thread that only exists inside a real running app). See test-coverage notes.")]
        public void PasswordConfValidator_Check_MatchesReferenceEntry()
        {
            var validator = new PasswordConfValidator { ReferenceEntry = new Entry { Text = "Passw0rd!" } };
            Assert.True(validator.Check("Passw0rd!"));
            Assert.False(validator.Check("Different1!"));
        }

        [Theory]
        [InlineData("", true)]
        [InlineData("SW1A 1AA", true)]
        [InlineData("not a postcode", false)]
        public void PostCodeValidator_Check(string value, bool expected)
        {
            Assert.Equal(expected, new PostCodeValidator().Check(value));
        }

        [Theory]
        [InlineData("", true)]
        // BUG: VatINValidator's regex embeds "# CountryName" comments inline without passing
        // RegexOptions.IgnorePatternWhitespace, so those comments are matched as literal required
        // text. As written, no realistic VAT number (including genuinely valid ones) can ever match.
        // Documenting actual behavior, not desired behavior.
        [InlineData("GB123456789", false)]
        [InlineData("not-a-vat-number", false)]
        public void VatINValidator_Check(string value, bool expected)
        {
            Assert.Equal(expected, new VatINValidator().Check(value));
        }

        [Theory]
        [InlineData("0", true)]
        [InlineData("5", true)]
        [InlineData("-1", false)]
        public void PickerRequiredValidator_Check(string value, bool expected)
        {
            Assert.Equal(expected, new PickerRequiredValidator().Check(value));
        }

        [Fact]
        public void PickerRequiredValidator_Check_ThrowsForNonNumeric()
        {
            Assert.Throws<NotSupportedException>(() => new PickerRequiredValidator().Check("not-a-number"));
        }
    }
}
