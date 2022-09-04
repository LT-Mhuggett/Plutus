using Plutus.Frontend.ClientUI.Core.Extensions;
using Plutus.Frontend.ClientUI.Resources.I18N_L10N;
using System;

namespace Plutus.Frontend.ClientUI.Core.Validators
{
    public class PickerRequiredValidator : IValidator
    {
        /// <inheritDoc/>
        /// <see cref="NatApp.Plutus.Helpers.Validators.IValidatorReqReference.Message"/>
        public string Message { get; set; } = Strings.FieldRequired;

        /// <summary>
        /// This implementation ensures <code>value</code> is not -1
        /// </summary>
        /// <inheritDoc/>
        /// <see cref="NatApp.Plutus.Helpers.Validators.IValidatorReqReference.Check(string)"/>
        public bool Check(string value)
        {
            if (int.TryParse(value, out int val))
                return val >= 0;
            throw new NotSupportedException("PickerRequiredValidator only works with Pickers");
        }
    }
}
