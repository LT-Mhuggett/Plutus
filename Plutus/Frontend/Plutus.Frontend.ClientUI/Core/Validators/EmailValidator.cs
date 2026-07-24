using System.Text.RegularExpressions;
using Plutus.Frontend.ClientUI.Core.Extensions;
using Plutus.Frontend.ClientUI.Resources.I18N_L10N;

namespace Plutus.Frontend.ClientUI.Core.Validators
{
    public class EmailValidator : IValidator
    {
        /// <inheritDoc/>
        public string Message { get; set; } = string.Format(Strings.NotValid, Strings.EMail);

        /// <summary>
        /// This implementation ensures <code>value</code> is a valid email
        /// </summary>
        /// <inheritDoc/>
        public bool Check(string value)
        {
            var regex = @"^[\w!#$%&'*+\-/=?\^_`{|}~]+(\.[\w!#$%&'*+\-/=?\^_`{|}~]+)*" + "@" + @"((([\-\w]+\.)+[a-zA-Z]{2,4})|(([0-9]{1,3}\.){3}[0-9]{1,3}))$";
            return Regex.IsMatch(value, regex);
        }
    }
}
