using Plutus.Frontend.ClientUI.Core.Extensions;
using Microsoft.Maui.Controls;
using Plutus.Frontend.ClientUI.Resources.I18N_L10N;

namespace Plutus.Frontend.ClientUI.Core.Validators
{
    public class PasswordConfValidator : IValidatorReqReference
    {
        public Entry ReferenceEntry { get; set; }

        /// <inheritDoc/>
        public string Message { get; set; } = string.Format(Strings.NotIdenticle, Strings.Password);

        /// <summary>
        /// This implementation ensures <code>value</code> is a valid password
        /// </summary>
        /// <inheritDoc/>
        public bool Check(string value)
        {
            return ReferenceEntry.Text.Equals(value);
        }
    }
}
