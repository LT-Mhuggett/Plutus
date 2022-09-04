using Plutus.Frontend.ClientUI.Resources.I18N_L10N;
using System.Globalization;

namespace Plutus.Frontend.ClientUI.XAML.Extensions
{
    public class DiscountTypeToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if(value is int discountType)
            {
                if (discountType == 0)
                    return Strings.Cash;
                else
                    return Strings.Percent;
            }
            throw new ArgumentException("value must be of Type int, for this operation.");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("This conversion operation is One Way from source.");
        }
    }
}
