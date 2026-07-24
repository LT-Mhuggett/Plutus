using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Helpers.Extensions.XAML
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
