using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace Plutus.Frontend.ClientUI.XAML.Extensions
{
    public class PickerIndexToDBIdConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int val)
                return val += 1;
            throw new NotSupportedException("PickerIndexToDBIDConverter only supports integers");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int val)
                return val -= 1;
            throw new NotSupportedException("PickerIndexToDBIDConverter only supports integers");
        }
    }
}
