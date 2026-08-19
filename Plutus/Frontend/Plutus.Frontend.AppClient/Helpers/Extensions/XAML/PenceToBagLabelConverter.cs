using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace Plutus.Frontend.AppClient.Helpers.Extensions.XAML
{
    /// <summary>
    /// A carrier-bag button's caption: pence in, <c>"Bag £0.10"</c> out (ruling 2026-08-19).
    ///
    /// ⚠⚠ THE WORDING IS THE WEB TILL'S, EXACTLY — <c>Bag {gbp(pricePence)}</c> in `TillPage.tsx`. Matt,
    /// 2026-08-19: *"I need the functionality and look and feel to be the same across both tills. So if a
    /// user swaps between the two, it doesnt matter and they would understand how to use it."* A cashier
    /// who learns "Bag 10p" on one till must not meet "Carrier bag (£0.10)" on the other.
    ///
    /// ⚠ Formatted at DISPLAY time, in the till's culture, from the integer pence the server sent —
    /// never stored pre-formatted. Money crosses the wire as pence everywhere in Plutus.
    ///
    /// ⚠ Never throws. A converter that throws inside a `BindableLayout` template takes out the whole
    /// row of buttons, and on a UI framework whose bindings fail silently that is a Bag button that
    /// simply is not there.
    /// </summary>
    public class PenceToBagLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var pence = System.Convert.ToInt64(value, CultureInfo.InvariantCulture);

                // ⚠ The till's own culture, not the invariant one — every other figure on this screen
                // uses `StringFormat='{0:C2}'`, which does the same.
                return "Bag " + (pence / 100m).ToString("C2", culture ?? CultureInfo.CurrentCulture);
            }
            catch
            {
                // ⚠ A button that says "Bag" still sells the right item — the command carries the bag,
                // not the caption. Losing the price is far better than losing the button.
                return "Bag";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException("A bag caption is display-only.");
    }
}
