using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Foundation;
using UIKit;
using I18N_L10N;
using System.Globalization;
using System.Threading;

[assembly:Xamarin.Forms.Dependency(typeof(Plutus.iOS.LocalizeImplementation))]
namespace Plutus.iOS
{
    class LocalizeImplementation : ILocalize
    {
        public void SetLocale(CultureInfo ci)
        {
            Thread.CurrentThread.CurrentCulture = ci;
            Thread.CurrentThread.CurrentUICulture = ci;

#if DEBUG
            Console.WriteLine($"CurrentCulture set: {ci.Name}");
#endif
        }

        public CultureInfo GetCurrentCultureInfo()
        {
            var netLanguage = "en";
            if (NSLocale.PreferredLanguages.LongLength > 0)
            {
                var pref = NSLocale.PreferredLanguages[0];
                netLanguage = iOSToDotnetLanguage(pref);
            }

            CultureInfo ci = null;
            try
            {
                ci = new CultureInfo(netLanguage);
            }
            catch (CultureNotFoundException e1)
            {
                try
                {
                    var fallback = ToDotnetFallbackLanguage(new PlatformCulture(netLanguage));
#if DEBUG
                    Console.WriteLine($"{netLanguage} failed, trying {fallback} ({e1.Message})");
#endif
                    ci = new CultureInfo(fallback);
                }
                catch (CultureNotFoundException e2)
                {
#if DEBUG
                    Console.WriteLine($"{netLanguage} couldn't be set, using 'en' ({e2.Message})");
#endif
                    ci = new CultureInfo("en");
                }
            }
            return ci;
        }

        private string iOSToDotnetLanguage(string iOSLanguage)
        {
#if DEBUG
            Console.WriteLine($".NET Language/Locale: {iOSLanguage}");
#endif
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
#if DEBUG
            Console.WriteLine($".NET Language/Locale: {netLanguage}");
#endif
            return netLanguage;
        }

        private string ToDotnetFallbackLanguage(PlatformCulture platformCulture)
        {
#if DEBUG
            Console.WriteLine($".NET Language/Locale: {platformCulture.LanguageCode}");
#endif
            var netLanguage = platformCulture.LanguageCode;
            switch (platformCulture.LanguageCode)
            {
                case "pt":
                    netLanguage = "pt-PT";
                    break;
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