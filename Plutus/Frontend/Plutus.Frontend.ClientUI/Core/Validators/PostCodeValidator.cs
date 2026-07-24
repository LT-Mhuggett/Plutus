using System.Text.RegularExpressions;
using Plutus.Frontend.ClientUI.Core.Extensions;
using Plutus.Frontend.ClientUI.Resources.I18N_L10N;

namespace Plutus.Frontend.ClientUI.Core.Validators
{
    class PostCodeValidator : IValidator
    {
        /// <inheritDoc/>
        public string Message { get; set; } = string.Format(Strings.FieldRequiredWithArg, Strings.PostCode);

        /// <summary>
        /// This implementation ensures <code>value</code> is a Valid UK PostCode
        /// </summary>
        /// <inheritDoc/>
        public bool Check(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return true;
            //UK PostCode regex
            var regex = @"^([Gg][Ii][Rr] 0[Aa]{2})|((([A-Za-z][0-9]{1,2})|(([A-Za-z][A-Ha-hJ-Yj-y][0-9]{1,2})|(([AZa-z][0-9][A-Za-z])|([A-Za-z][A-Ha-hJ-Yj-y][0-9]?[A-Za-z])))) [0-9][A-Za-z]{2})$";
            return Regex.IsMatch(value, regex);
        }
    }
}
