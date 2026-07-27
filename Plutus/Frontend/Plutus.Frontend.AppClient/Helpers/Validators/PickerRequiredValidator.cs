using Plutus.Frontend.AppClient.Helpers.Extensions;
using System;

namespace Plutus.Frontend.AppClient.Helpers.Validators
{
    public class PickerRequiredValidator : IValidator
    {
        /// <inheritDoc/>
        /// <see cref="Plutus.Frontend.AppClient.Helpers.Validators.IValidatorReqReference.Message"/>
        public string Message { get; set; } = "FieldRequired".Translate();

        /// <summary>
        /// This implementation ensures <code>value</code> is not -1
        /// </summary>
        /// <inheritDoc/>
        /// <see cref="Plutus.Frontend.AppClient.Helpers.Validators.IValidatorReqReference.Check(string)"/>
        public bool Check(string value)
        {
            if (int.TryParse(value, out int val))
                return val >= 0;
            throw new NotSupportedException("PickerRequiredValidator only works with Pickers");
        }
    }
}
