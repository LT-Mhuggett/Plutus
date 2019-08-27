using System;
using System.Globalization;
using System.IO;
using Xamarin.Forms;

namespace NatApp.Plutus.Helpers.Extensions.XAML
{
    public class ByteArrayToImageSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is byte[] imageData)
            {
                return ImageSource.FromStream(() => new MemoryStream(imageData));
            }
            else if (value == null)
                return null;
            throw new ArgumentException("value must be of Type Byte[], for this operation!");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("This conversion opertaion is One Way from source!");
        }
    }
}
