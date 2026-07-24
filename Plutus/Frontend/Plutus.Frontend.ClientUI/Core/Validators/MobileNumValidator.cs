using System.Text.RegularExpressions;
using Plutus.Frontend.ClientUI.Core.Extensions;
using Plutus.Frontend.ClientUI.Resources.I18N_L10N;

namespace Plutus.Frontend.ClientUI.Core.Validators
{
    class MobileNumValidator : IValidator
    {
        /// <inheritDoc/>
        public string Message { get; set; } = string.Format(Strings.NotValid, Strings.MobileNum);

        /// <summary>
        /// This implementation ensures <code>value</code> is a mobile number in a given region
        /// </summary>
        /// <inheritDoc/>
        public bool Check(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return true;
            //Currently Validates UK numbers, Possible I10N_L18N for other nations
            var regex = @"^(((\+44\s?\d{4}|\(?0\d{4}\)?)\s?\d{3}\s?\d{3})|((\+44\s?\d{3}|\(?0\d{3}\)?)\s?\d{3}\s?\d{4})|((\+44\s?\d{2}|\(?0\d{2}\)?)\s?\d{4}\s?\d{4}))(\s?\#(\d{4}|\d{3}))?$";
            return Regex.IsMatch(value, regex);
        }
    }
}
