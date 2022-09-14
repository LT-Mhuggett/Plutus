using System.Text.RegularExpressions;
using Plutus.Frontend.ClientUI.Core.Extensions;
using Plutus.Frontend.ClientUI.Resources.I18N_L10N;

namespace Plutus.Frontend.ClientUI.Core.Validators
{
    class VatINValidator : IValidator
    {
        /// <inheritDoc/>
        public string Message { get; set; } = string.Format(Strings.FieldRequiredWithArg, Strings.VatIN);

        /// <summary>
        /// This implementation ensures <code>value</code> is a Valid EU Vat IN
        /// </summary>
        /// <inheritDoc/>
        public bool Check(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return true;
            value = Regex.Replace(value, @"[-.●]", "");
            //EU VatIN regex
            var regex = @"^("+
                           "(AT)?U[0-9]{8} |                              # Austria" +
                           "(BE)?0[0-9]{9} |                              # Belgium" +
                           "(BG)?[0-9]{9,10} |                            # Bulgaria" +
                           "(CY)?[0-9]{8}L |                              # Cyprus" +
                           "(CZ)?[0-9]{8,10} |                            # Czech Republic" +
                           "(DE)?[0-9]{9} |                               # Germany" +
                           "(DK)?[0-9]{8} |                               # Denmark" +
                           "(EE)?[0-9]{9} |                               # Estonia" +
                           "(EL|GR)?[0-9]{9} |                            # Greece" +
                           "(ES)?[0-9A-Z][0-9]{7}[0-9A-Z] |               # Spain" +
                           "(FI)?[0-9]{8} |                               # Finland" +
                           "(FR)?[0-9A-Z]{2}[0-9]{9} |                    # France" +
                           "(GB)?([0-9]{9}([0-9]{3})?|[A-Z]{2}[0-9]{3}) | # United Kingdom" +
                           "(HU)?[0-9]{8} |                               # Hungary" +
                           "(IE)?[0-9]S[0-9]{5}L |                        # Ireland" +
                           "(IT)?[0-9]{11} |                              # Italy" +
                           "(LT)?([0-9]{9}|[0-9]{12}) |                   # Lithuania" +
                           "(LU)?[0-9]{8} |                               # Luxembourg" +
                           "(LV)?[0-9]{11} |                              # Latvia" +
                           "(MT)?[0-9]{8} |                               # Malta" +
                           "(NL)?[0-9]{9}B[0-9]{2} |                      # Netherlands" +
                           "(PL)?[0-9]{10} |                              # Poland" +
                           "(PT)?[0-9]{9} |                               # Portugal" +
                           "(RO)?[0-9]{2,10} |                            # Romania" +
                           "(SE)?[0-9]{12} |                              # Sweden" +
                           "(SI)?[0-9]{8} |                               # Slovenia" +
                           "(SK)?[0-9]{10}                                # Slovakia" +
                           ")$";
            return Regex.IsMatch(value, regex);
        }
    }
}
