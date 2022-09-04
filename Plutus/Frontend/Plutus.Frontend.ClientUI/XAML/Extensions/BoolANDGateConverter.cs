using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace Plutus.Frontend.ClientUI.XAML.Extensions
{
    public class BoolANDGateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool val1 && parameter is bool val2)
                return val1 & val2;
            throw new ArgumentException("Value and Parameter must both be boolean type!");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("This converter is oneway from source only");
        }
    }
}
