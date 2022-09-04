using System.Text.RegularExpressions;
using Plutus.Frontend.ClientUI.Core.Extensions;
using Plutus.Frontend.ClientUI.Resources.I18N_L10N;

namespace Plutus.Frontend.ClientUI.Core.Validators
{
    class NiNoValidator : IValidator
    {
        /// <inheritDoc/>
        /// <see cref="NatApp.Plutus.Helpers.Validators.IValidatorReqReference.Message"/>
        public string Message { get; set; } = string.Format(Strings.FieldRequiredWithArg, Strings.NiNo);

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
