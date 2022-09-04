using Plutus.Frontend.ClientUI.Core.Extensions;
using Plutus.Frontend.ClientUI.Resources.I18N_L10N;

namespace Plutus.Frontend.ClientUI.Core.Validators
{
    class DecimalValidator : IValidator
    {
        /// <inheritDoc/>
        /// <see cref="NatApp.Plutus.Helpers.Validators.IValidatorReqReference.Message"/>
        public string Message { get; set; } = string.Format(Strings.NotValid, Strings.NumVal);

        /// <summary>
        /// This implementation ensures <code>value</code> is a decimal value
        /// </summary>
        /// <inheritDoc/>
        /// <see cref="NatApp.Plutus.Helpers.Validators.IValidatorReqReference.Check(string)"/>
        public bool Check(string value)
        {
            return decimal.TryParse(value, out _);
        }
    }
}
