using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Plutus.Frontend.AppClient.Helpers.Extensions;

namespace Plutus.Frontend.AppClient.Helpers.Validators
{
    public class EmailValidator : IValidator
    {
        /// <inheritDoc/>
        /// <see cref="Plutus.Frontend.AppClient.Helpers.Validators.IValidatorReqReference.Message"/>
        public string Message { get; set; } = string.Format("NotValid".Translate(), "EMail".Translate());

        /// <summary>
        /// This implementation ensures <code>value</code> is a valid email
        /// </summary>
        /// <inheritDoc/>
        /// <see cref="Plutus.Frontend.AppClient.Helpers.Validators.IValidatorReqReference.Check(string)"/>
        public bool Check(string value)
        {
            var regex = @"^[\w!#$%&'*+\-/=?\^_`{|}~]+(\.[\w!#$%&'*+\-/=?\^_`{|}~]+)*" + "@" + @"((([\-\w]+\.)+[a-zA-Z]{2,4})|(([0-9]{1,3}\.){3}[0-9]{1,3}))$";
            return Regex.IsMatch(value, regex);
        }
    }
}
