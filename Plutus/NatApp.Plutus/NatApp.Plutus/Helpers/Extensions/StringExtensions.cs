using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NatApp.Plutus.Helpers.Extensions
{
    /// <summary>
    /// All Extensions for string
    /// </summary>
    public static class StringExtensions
    {
        public static string Translate(this string str, string[] valueArray = null)
        {
            if(valueArray == null)
            {

                return App.translateExtension.ProvideValue(str);
            }
            else
            {
                return string.Format(App.translateExtension.ProvideValue(str), valueArray);
            }
        }

        public static bool IsNumeric(this string value)
        {
            return value.All(char.IsNumber);
        }

        public static bool Contains(this string source, string toCheck, StringComparison stringComparison) => source?.IndexOf(toCheck, stringComparison) >= 0;
    }
}
