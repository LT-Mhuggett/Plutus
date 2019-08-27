using System;
using System.Collections.Generic;
using System.Text;
using NatApp.Plutus.Helpers.Extensions;

namespace NatApp.Plutus.Helpers.Validators
{
    public class RequiredValidator : IValidator
    {
        /// <inheritDoc/>
        /// <see cref="NatApp.Plutus.Helpers.Validators.IValidatorReqReference.Message"/>
        public string Message { get; set; } = "FieldRequired".Translate();

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
