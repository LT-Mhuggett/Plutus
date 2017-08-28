using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace Plutus.Helpers
{
    class Conversions
    {
        internal static async Task<decimal> ToDecimal(string data)
        {
            try
            {
                decimal result = Convert.ToDecimal(data);
                return result;
            }
            catch (FormatException)
            {
                await Application.Current.MainPage.DisplayAlert("OOPS!", "It seams that your Cost or Price have been inputed wrong/nPlease check this/nThis Message will be change in future", "OK");
                return 0.0m;
            }
            catch (OverflowException)
            {
                await Application.Current.MainPage.DisplayAlert("OOPS!", "It seams that your Cost or Price have been inputed wrong/nPlease check this/nThis Message will be change in future", "OK");
                return 0.0m;
            }

        }

        internal static async Task<int> ToInterger(string data)
        {
            try
            {
                int result = Convert.ToInt16(data);
                return result;
            }
            catch (FormatException)
            {
                await Application.Current.MainPage.DisplayAlert("OOPS!", "It seams that your Stock has been inputed wrong/nPlease check this/nThis Message will be change in future", "OK");
                return -1;
            }
            catch (OverflowException)
            {
                await Application.Current.MainPage.DisplayAlert("OOPS!", "It seams that your Stock has been inputed wrong/nPlease check this/nThis Message will be change in future", "OK");
                return -1;
            }
        }
    }
}
