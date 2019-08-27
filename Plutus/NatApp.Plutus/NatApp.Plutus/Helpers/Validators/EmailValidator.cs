using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using NatApp.Plutus.Helpers.Extensions;

namespace NatApp.Plutus.Helpers.Validators
{
    public class EmailValidator : IValidator
    {
        /// <inheritDoc/>
        /// <see cref="NatApp.Plutus.Helpers.Validators.IValidatorReqReference.Message"/>
        public string Message { get; set; } = string.Format("NotValid".Translate(), "Email".Translate());

        /// <summary>
        /// This implementation ensures <code>value</code> is a valid email
        /// </summary>
        /// <inheritDoc/>
        /// <see cref="NatApp.Plutus.Helpers.Validators.IValidatorReqReference.Check(string)"/>
        public bool Check(string value)
        {
            var regex = @"^[\w!#$%&'*+\-/=?\^_`{|}~]+(\.[\w!#$%&'*+\-/=?\^_`{|}~]+)*" + "@" + @"((([\-\w]+\.)+[a-zA-Z]{2,4})|(([0-9]{1,3}\.){3}[0-9]{1,3}))$";
            return Regex.IsMatch(value, regex);
        }
    }
}
