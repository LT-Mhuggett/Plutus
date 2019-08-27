using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Xamarin.Forms;

namespace NatApp.Plutus.Helpers.Extensions.XAML
{
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(bool)value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => !(bool)value;
    }
}
