using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Xamarin.Forms;

namespace NatApp.Plutus.Helpers.Extensions.XAML
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
