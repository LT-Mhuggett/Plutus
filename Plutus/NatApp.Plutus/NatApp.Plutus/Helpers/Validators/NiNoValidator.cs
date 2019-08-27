using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using NatApp.Plutus.Helpers.Extensions;

namespace NatApp.Plutus.Helpers.Validators
{
    class NiNoValidator : IValidator
    {
        /// <inheritDoc/>
        /// <see cref="NatApp.Plutus.Helpers.Validators.IValidatorReqReference.Message"/>
        public string Message { get; set; } = string.Format("FieldRequiredWithArg".Translate(), "NiNo".Translate());

        /// <summary>
        /// This implementation ensures <code>value</code> is a Valid UK NIN
        /// </summary>
        /// <inheritDoc/>
        /// <see cref="NatApp.Plutus.Helpers.Validators.IValidatorReqReference.Check(string)"/>
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
