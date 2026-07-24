using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;

namespace I18N_L10N
{
    public class I18N_L10N
    {
        public static void SetCulture()
        {
            if (DeviceInfo.Platform == DevicePlatform.iOS || DeviceInfo.Platform == DevicePlatform.Android)
            {
                var localize = IPlatformApplication.Current!.Services.GetRequiredService<ILocalize>();
                var ci = localize.GetCurrentCultureInfo();
                Resx.AppResources.Culture = ci;
                localize.SetLocale(ci);
            }
        }
    }
}
