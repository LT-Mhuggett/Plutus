using NatApp.Plutus.Helpers.Extensions;
using System;
using System.Collections.Generic;
using System.Text;

namespace NatApp.Plutus.Helpers.Validators
{
    public class IntegerValidator : IValidator
    {
        /// <inheritDoc/>
        /// <see cref="NatApp.Plutus.Helpers.Validators.IValidatorReqReference.Message"/>
        public string Message { get; set; } = string.Format("NotValid".Translate(), "NumVal".Translate());

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
