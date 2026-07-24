using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace Plutus.Frontend.ClientUI.XAML.Extensions
{
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(bool)value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => !(bool)value;
    }
}
