using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace Plutus.Helpers
{
    class Conversions
    {
        internal static async Task<decimal?> ToDecimal(string data, string ErrorMesg)
        {
            try
            {
                decimal result = Convert.ToDecimal(data);
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

        internal static async Task<int?> ToInterger(string data, string ErrorMesg)
        {
            try
            {
                int result = Convert.ToInt16(data);
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
    }
}
