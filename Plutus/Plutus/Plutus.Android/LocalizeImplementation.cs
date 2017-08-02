using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using I18N_L10N;
using System.Threading;

[assembly:Xamarin.Forms.Dependency(typeof(Plutus.Droid.LocalizeImplementation))]
namespace Plutus.Droid
{

    class LocalizeImplementation : ILocalize
    {
        public void SetLocale(CultureInfo ci)
        {
            Thread.CurrentThread.CurrentCulture = ci;
            Thread.CurrentThread.CurrentUICulture = ci;
#if DEBUG
            Console.WriteLine($"Current Culture Set: {ci.Name}");
#endif

        }

        public CultureInfo GetCurrentCultureInfo()
        {
            var netLanguage = "en";
            var droidLocale = Java.Util.Locale.Default;
            netLanguage = DroidToDotnetLanguage(droidLocale.ToString().Replace("_", "-"));
            CultureInfo ci = null;
            try
            {
                ci = new CultureInfo(netLanguage);
            }
            catch(CultureNotFoundException e1)
            {
                try
                {
                    var fallback = ToDotNetFallbackLanguage(new PlatformCulture(netLanguage));
#if DEBUG
                    Console.WriteLine($"{netLanguage} failed, trying {fallback} ({e1.Message})");
#endif
                    ci = new CultureInfo(fallback);
                }
                catch(CultureNotFoundException e2)
                {
#if DEBUG
                    Console.WriteLine($"{netLanguage} couldn't be set, using 'en' ({e2.Message})");
#endif
                    ci = new CultureInfo("en");
                }
            }
            return ci;
        }

        private string DroidToDotnetLanguage(string droidLanguage)
        {
#if DEBUG
            Console.WriteLine($"Android Language: {droidLanguage}");
#endif
            var netLanguage = droidLanguage;

            switch (droidLanguage)
            {
                case "ms-BN":
                case "ms-my":
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
#if DEBUG
                    Console.WriteLine($".NET Language/Locale: {netLanguage}");
#endif
            return netLanguage;
        }

        private string ToDotNetFallbackLanguage(PlatformCulture platformCulture)
        {
#if DEBUG
            Console.WriteLine($".NET Fallback Language: {platformCulture.LanguageCode}");
#endif
            var netLanguage = platformCulture.LanguageCode;
            switch (platformCulture.LanguageCode)
            {
                case "gsw":
                    netLanguage = "de-CH";
                    break;
            }
#if DEBUG
            Console.WriteLine($".NET Fallback Language/Locale: {netLanguage} (application-specific)");
#endif
            return netLanguage;
        }
    }
}