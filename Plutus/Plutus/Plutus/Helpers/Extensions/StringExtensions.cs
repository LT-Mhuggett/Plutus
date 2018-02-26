using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace Plutus.Helpers.Extensions
{
    public static class StringExtenstions
    {
        public static async Task<decimal?> ToDecimal(this string data, string ErrorMesg)
        {
            try
            {
                var result = Convert.ToDecimal(data);
                return result;
            }
            catch (FormatException)
            {
                await Application.Current.MainPage.DisplayAlert("OOPS!", ErrorMesg, "OK");
                return null;
            }
            catch (OverflowException)
            {
                await Application.Current.MainPage.DisplayAlert("OOPS!", ErrorMesg, "OK");
                return null;
            }

        }

        public static async Task<int?> ToInterger(this string data, string ErrorMesg)
        {
            try
            {
                int result = Convert.ToInt32(data);
                return result;
            }
            catch (FormatException)
            {
                await Application.Current.MainPage.DisplayAlert("OOPS!", ErrorMesg, "OK");
                return null;
            }
            catch (OverflowException)
            {
                await Application.Current.MainPage.DisplayAlert("OOPS!", ErrorMesg, "OK");
                return null;
            }
        }

        public static async Task<double?> ToDouble(this string data, string ErrorMesg)
        {
            try
            {
                var result = Convert.ToDouble(data);
                return result;
            }
            catch (FormatException)
            {
                await Application.Current.MainPage.DisplayAlert("OOPS!", ErrorMesg, "OK");
                return null;
            }
            catch (OverflowException)
            {
                await Application.Current.MainPage.DisplayAlert("OOPS!", ErrorMesg, "OK");
                return null;
            }
        }

        public static bool IsNumeric(this string value)
        {
            return value.All(char.IsNumber);
        }

        internal static async Task<bool> PasswordCheck(this string pass1, string pass2)
        {
            if (pass1 != null && pass2 != null)
                if (pass1.Equals(pass2))
                {
                    if (Validate.IsValidPassword(pass1))
                        return true;
                    await Application.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Oops"),
                        App.Translate.ProvideValue("PassWNotStrong"),
                        App.Translate.ProvideValue("OK"));
                    return false;
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Oops"),
                        App.Translate.ProvideValue("PassWNotSameMesg"),
                        App.Translate.ProvideValue("OK"));
                    return false;
                }

            await Application.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Oops"),
                App.Translate.ProvideValue("SetPassWMesg"),
                App.Translate.ProvideValue("OK"));
            return false;
        }
    }
}
