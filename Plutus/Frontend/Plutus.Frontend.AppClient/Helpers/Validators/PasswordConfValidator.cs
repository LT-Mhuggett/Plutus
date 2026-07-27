using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Helpers.Validators
{
    public class PasswordConfValidator : IValidatorReqReference
    {
        public Entry ReferenceEntry { get; set; }

        /// <inheritDoc/>
        /// <see cref="Plutus.Frontend.AppClient.Helpers.Validators.IValidatorReqReference.Message"/>
        public string Message { get; set; } = string.Format("NotIdenticle".Translate(), "Password".Translate());

        /// <summary>
        /// This implementation ensures <code>value</code> is a valid password
        /// </summary>
        /// <inheritDoc/>
        /// <see cref="Plutus.Frontend.AppClient.Helpers.Validators.IValidatorReqReference.Check(string)"/>
        public bool Check(string value)
        {
            return ReferenceEntry.Text.Equals(value);
        }
    }
}
