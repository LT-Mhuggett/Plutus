using System.Globalization;
using Xamarin.Forms;

namespace I18N_L10N
{
    public class I18N_L10N
    {
        public static void SetCulture()
        {
            if (Device.RuntimePlatform == Device.iOS || Device.RuntimePlatform == Device.Android)
            {
                var ci = DependencyService.Get<ILocalize>().GetCurrentCultureInfo();
                Resx.AppResources.Culture = ci;
                DependencyService.Get<ILocalize>().SetLocale(ci);
            }
        }
    }
}
