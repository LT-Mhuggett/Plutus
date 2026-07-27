using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Plutus.Frontend.AppClient.Helpers.Extensions;

namespace Plutus.Frontend.AppClient.Helpers.Validators
{
    class NiNoValidator : IValidator
    {
        /// <inheritDoc/>
        /// <see cref="Plutus.Frontend.AppClient.Helpers.Validators.IValidatorReqReference.Message"/>
        public string Message { get; set; } = string.Format("FieldRequiredWithArg".Translate(), "NiNo".Translate());

        /// <summary>
        /// This implementation ensures <code>value</code> is a Valid UK NIN
        /// </summary>
        /// <inheritDoc/>
        /// <see cref="Plutus.Frontend.AppClient.Helpers.Validators.IValidatorReqReference.Check(string)"/>
        public bool Check(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return true;
            //UK NIN regex
            var regex = @"^(?!BG)(?!GB)(?!NK)(?!KN)(?!TN)(?!NT)(?!ZZ)[A-Z&&[^DFIQUV]][A-Z&&[^DFIOQUV]][0-9]{6}[ABCD ]?$";
            return Regex.IsMatch(value, regex);
        }
    }
}
