using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace Plutus.Frontend.ClientUI.XAML.Extensions
{
    public class StringNullEmptyBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string @string)
                return (bool)!string.IsNullOrEmpty(@string);
            else
                throw new ArgumentException("Value type is not valid in this converter", "value");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("This converter is onweay from source only");
        }
    }
}
