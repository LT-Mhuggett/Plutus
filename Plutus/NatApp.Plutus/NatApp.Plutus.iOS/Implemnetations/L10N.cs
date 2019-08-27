using System.Globalization;
using System.Threading;
using Foundation;
using I18N_L10N;
using Xamarin.Forms;

[assembly: Dependency(typeof(NatApp.Plutus.iOS.Implemnetations.L10N))]
namespace NatApp.Plutus.iOS.Implemnetations
{
    public class L10N : ILocalize
    {
        public void SetLocale(CultureInfo ci)
        {
            Thread.CurrentThread.CurrentCulture = ci;
            Thread.CurrentThread.CurrentUICulture = ci;
        }

        public CultureInfo GetCurrentCultureInfo()
        {
            var netLanguage = "en";
            if (NSLocale.PreferredLanguages.Length > 0)
            {
                var pref = NSLocale.PreferredLanguages[0];
                netLanguage = iOSToDotnetLanguage(pref);
            }

            CultureInfo ci = null;
            try
            {
                ci = new CultureInfo(netLanguage);
            }
            catch(CultureNotFoundException ex)
            {
                try
                {
                    var fallback = ToDotnetFallbackLanguage(new PlatformCulture(netLanguage));
                    ci = new CultureInfo(fallback);
                }
                catch(CultureNotFoundException ex2)
                {
                    ci = new CultureInfo("en");
                }
            }
            return ci;
        }

#pragma warning disable IDE1006 // Naming Styles, Reason: iOS is a name and cannot fit naming convention
        private string iOSToDotnetLanguage(string iOSLanguage)
#pragma warning restore IDE1006 // Naming Styles
        {
            var netLanguage = iOSLanguage;

            switch (iOSLanguage)
            {
                case "ms-MY":
                case "ms-SG":
                    netLanguage = "ms";
                    break;
                case "gsw-CH":
                    netLanguage = "de-CH";
                    break;
            }
            return netLanguage;
        }

        private string ToDotnetFallbackLanguage(PlatformCulture platformCulture)
        {
            var netLanguage = platformCulture.LanguageCode;
            switch (platformCulture.LanguageCode)
            {
                case "pt":
                    netLanguage = "pt-PT";
                    break;
                case "gsw":
                    netLanguage = "de-ch";
                    break;
            }
            return netLanguage;
        }
    }
}