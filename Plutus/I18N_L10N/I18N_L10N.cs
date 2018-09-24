using Xamarin.Forms;

namespace I18N_L10N
{
    public class I18N_L10N
    {
        public I18N_L10N()
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
