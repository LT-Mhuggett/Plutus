using NatApp.Plutus.Helpers.Extensions;
using System;

namespace NatApp.Plutus.Helpers.Validators
{
    public class PickerRequiredValidator : IValidator
    {
        /// <inheritDoc/>
        /// <see cref="NatApp.Plutus.Helpers.Validators.IValidatorReqReference.Message"/>
        public string Message { get; set; } = "FieldRequired".Translate();

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
