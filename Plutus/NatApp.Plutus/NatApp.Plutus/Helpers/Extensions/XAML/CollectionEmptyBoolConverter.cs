using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Xamarin.Forms;

namespace NatApp.Plutus.Helpers.Extensions.XAML
{
    public class CollectionEmptyBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ICollection collection)
                return collection.Count == 0 ? false : true;
            else
                throw new ArgumentException("Value type not supported", "value");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("Cannot convert from Boolean to Collection");
        }
    }
}
