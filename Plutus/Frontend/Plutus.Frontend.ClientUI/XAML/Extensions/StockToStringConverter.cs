using Plutus.Entities.Models;
using System.Globalization;

namespace Plutus.Frontend.ClientUI.XAML.Extensions
{
    public class StockToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return "∞";
            else if (value is Stock stock)
                return stock.Quantity;
            else
                throw new Exception("Value is not of type Stock");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
