using System.Globalization;
using System.Threading;
using I18N_L10N;
using Xamarin.Forms;

[assembly: Dependency(typeof(NatApp.Plutus.Droid.Implementations.L10N))]
namespace NatApp.Plutus.Droid.Implementations
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
            var droidLocale = Java.Util.Locale.Default;
            netLanguage = AndroidToDotenetLanguage(droidLocale.ToString().Replace("_", "-"));

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

        private string AndroidToDotenetLanguage(string droidLanguage)
        {
            var netLanguage = droidLanguage;
            switch (droidLanguage)
            {
                case "ms-BN":
                case "ms-MY":
                case "ms-SG":
                    netLanguage = "ms";
                    break;
                case "in-ID":
                    netLanguage = "id-ID";
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
                case "gsw":
                    netLanguage = "de-CH";
                    break;
            }
            return netLanguage;
        }
    }
}