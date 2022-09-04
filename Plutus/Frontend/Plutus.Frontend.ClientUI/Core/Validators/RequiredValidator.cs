using Plutus.Frontend.ClientUI.Core.Extensions;
using Plutus.Frontend.ClientUI.Resources.I18N_L10N;

namespace Plutus.Frontend.ClientUI.Core.Validators
{
    public class RequiredValidator : IValidator
    {
        /// <inheritDoc/>
        /// <see cref="NatApp.Plutus.Helpers.Validators.IValidatorReqReference.Message"/>
        public string Message { get; set; } = Strings.FieldRequired;

        /// <summary>
        /// This implementation ensures <code>value</code> is not null or whitespace
        /// </summary>
        /// <inheritDoc/>
        /// <see cref="NatApp.Plutus.Helpers.Validators.IValidatorReqReference.Check(string)"/>
        public bool Check(string value)
        {
            return !string.IsNullOrWhiteSpace(value);
        }
    }
}
