using Plutus.Frontend.AppClient.Helpers.Extensions;

namespace Plutus.Frontend.AppClient.Helpers.Validators
{
    class DecimalValidator : IValidator
    {
        /// <inheritDoc/>
        /// <see cref="Plutus.Frontend.AppClient.Helpers.Validators.IValidatorReqReference.Message"/>
        public string Message { get; set; } = string.Format("NotValid".Translate(), "NumVal".Translate());

        /// <summary>
        /// This implementation ensures <code>value</code> is a decimal value
        /// </summary>
        /// <inheritDoc/>
        /// <see cref="Plutus.Frontend.AppClient.Helpers.Validators.IValidatorReqReference.Check(string)"/>
        public bool Check(string value)
        {
            return decimal.TryParse(value, out _);
        }
    }
}
