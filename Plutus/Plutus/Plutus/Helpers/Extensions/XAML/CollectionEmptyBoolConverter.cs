using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Xamarin.Forms;

namespace Plutus.Helpers.Extensions.XAML
{
    class CollectionEmptyBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ObservableCollection<Tuple<string, decimal>>)
            {
                if ((value as ObservableCollection<Tuple<string, decimal>>).Count != 0)
                    return true;
                return false;
            }
            else if (value == null)
                return false;
            throw new ArgumentException();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
