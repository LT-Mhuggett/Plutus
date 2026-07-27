using Plutus.Frontend.AppClient.Helpers.Extensions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Plutus.Frontend.AppClient.Helpers.Validators
{
    public class IntegerValidator : IValidator
    {
        /// <inheritDoc/>
        /// <see cref="Plutus.Frontend.AppClient.Helpers.Validators.IValidatorReqReference.Message"/>
        public string Message { get; set; } = string.Format("NotValid".Translate(), "NumVal".Translate());

        /// <summary>
        /// This implementation ensures <code>value</code> is a integer value
        /// </summary>
        /// <inheritDoc/>
        /// <see cref="Plutus.Frontend.AppClient.Helpers.Validators.IValidatorReqReference.Check(string)"/>
        public bool Check(string value)
        {
            return int.TryParse(value, out _);
        }
    }
}
