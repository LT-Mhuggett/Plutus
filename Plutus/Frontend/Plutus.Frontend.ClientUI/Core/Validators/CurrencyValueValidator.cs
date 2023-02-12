using Plutus.Frontend.ClientUI.Core.Extensions;
using Plutus.Frontend.ClientUI.Resources.I18N_L10N;
using System.Globalization;

namespace Plutus.Frontend.ClientUI.Core.Validators
{
    public class CurrencyValueValidator : IValidator
    {
        #region Fields
        NumberStyles _numberStyle;
        #endregion

        /// <summary>
        /// Create a currency Validator
        /// </summary>
        /// <remarks>
        /// Default Currency acceptance:
        /// Allow Currency Symbol
        /// Allow Thousands
        /// Allow Decimal Point
        /// </remarks>
        public CurrencyValueValidator()
        {
            _numberStyle = NumberStyles.AllowCurrencySymbol | NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint;
        }

        /// <summary>
        /// Create a currency Validator
        /// </summary>
        /// <param name="numberStyles">Currency Acceptance parameters</param>
        public CurrencyValueValidator(params NumberStyles[] numberStyles)
        {
            _numberStyle = NumberStyles.None;
            foreach(var numStyle in numberStyles)
            {
                _numberStyle |= numStyle;
            }
        }

        /// <summary>
        /// Create a currency Validator
        /// </summary>
        /// <param name="numberStyle">Currency Acceptance parameters</param>
        public CurrencyValueValidator(NumberStyles numberStyle)
        {
            _numberStyle = numberStyle;
        }
        
        /// <inheritDoc/>
        public string Message { get; set; } = string.Format(Strings.NotValid, Strings.NumAndCurrencyVal);

        /// <summary>
        /// This implementation ensures <code>value</code> is a Currency and decimal value
        /// </summary>
        /// <inheritDoc/>
        public bool Check(string value)
        {
            return decimal.TryParse(value, _numberStyle, CultureInfo.CurrentCulture, out decimal _discard);
        }
    }
}
