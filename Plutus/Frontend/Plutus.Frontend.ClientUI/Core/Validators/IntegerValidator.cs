using Plutus.Frontend.ClientUI.Core.Extensions;
using Plutus.Frontend.ClientUI.Resources.I18N_L10N;

namespace Plutus.Frontend.ClientUI.Core.Validators
{
    public class IntegerValidator : IValidator
    {
        /// <inheritDoc/>
        /// <see cref="NatApp.Plutus.Helpers.Validators.IValidatorReqReference.Message"/>
        public string Message { get; set; } = string.Format(Strings.NotValid, Strings.NumVal);

        /// <summary>
        /// This implementation ensures <code>value</code> is a integer value
        /// </summary>
        /// <inheritDoc/>
        /// <see cref="NatApp.Plutus.Helpers.Validators.IValidatorReqReference.Check(string)"/>
        public bool Check(string value)
        {
            return int.TryParse(value, out _);
        }
    }
}
