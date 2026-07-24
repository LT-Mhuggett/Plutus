using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Plutus.Frontend.AppClient.Helpers.Extensions
{
    /// <summary>
    /// All Extensions for string
    /// </summary>
    public static class StringExtensions
    {
        /// <summary>
        /// Execute a translation of the string using App.TranslateExtension
        /// </summary>
        /// <param name="str">Value to translate</param>
        /// <param name="valueArray">Values to place in a format viable string</param>
        /// <returns>Translated string</returns>
        public static string Translate(this string str, string[] valueArray = null)
        {
            if(valueArray == null)
            {

                return App.TranslateExtension.ProvideValue(str);
            }
            else
            {
                return string.Format(App.TranslateExtension.ProvideValue(str), valueArray);
            }
        }

        /// <summary>
        /// Checks if string is numeric 
        /// </summary>
        /// <param name="value">value to check</param>
        /// <returns></returns>
        public static bool IsNumeric(this string value) => value.All(char.IsNumber);

        /// <summary>
        /// Checks if string contains a given string, using a specified string comparator
        /// </summary>
        /// <param name="source">value being tested</param>
        /// <param name="toCheck">value to test against</param>
        /// <param name="stringComparison">comparator to use</param>
        /// <returns>true = when source string contains the sub-string according to the comparator; false = is not present in contained string</returns>
        public static bool Contains(this string source, string toCheck, StringComparison stringComparison) => source?.IndexOf(toCheck, stringComparison) >= 0;

        /// <summary>
        /// Generates a stream from a given string
        /// </summary>
        /// <param name="str">String to convert to stream</param>
        /// <returns>string data as a stream</returns>
        public static MemoryStream ToStream(this string str)
        {
            MemoryStream stream = new MemoryStream();
            StreamWriter writer = new StreamWriter(stream);
            writer.Write(str);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }
    }
}
