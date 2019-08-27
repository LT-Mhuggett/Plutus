using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using NatApp.Plutus.Helpers.Extensions;

namespace NatApp.Plutus.Helpers.Validators
{
    class MobileNumValidator : IValidator
    {
        /// <inheritDoc/>
        /// <see cref="NatApp.Plutus.Helpers.Validators.IValidatorReqReference.Message"/>
        public string Message { get; set; } = string.Format("NotValid".Translate(), "MobileNum".Translate());

        /// <summary>
        /// This implementation ensures <code>value</code> is a mobile number in a given region
        /// </summary>
        /// <inheritDoc/>
        /// <see cref="NatApp.Plutus.Helpers.Validators.IValidatorReqReference.Check(string)"/>
        public bool Check(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return true;
            //Currently Validates UK numbers, Possible I10N_L18N for other nations
            var regex = @"^(((\+44\s?\d{4}|\(?0\d{4}\)?)\s?\d{3}\s?\d{3})|((\+44\s?\d{3}|\(?0\d{3}\)?)\s?\d{3}\s?\d{4})|((\+44\s?\d{2}|\(?0\d{2}\)?)\s?\d{4}\s?\d{4}))(\s?\#(\d{4}|\d{3}))?$";
            return Regex.IsMatch(value, regex);
        }
    }
}
