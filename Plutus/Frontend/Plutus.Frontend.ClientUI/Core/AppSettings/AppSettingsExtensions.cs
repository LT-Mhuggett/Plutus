using Microsoft.Extensions.Configuration;
using Microsoft.Maui.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Plutus.Frontend.ClientUI.Core.AppSettings
{
    public static class AppSettingsExtensions
    {
        public static MauiAppBuilder ConfigureAppSettings(this MauiAppBuilder builder)
        {
            var assembly = Assembly.GetExecutingAssembly();

            Stream appSettingsResourceItem = null;

#if !DEBUG
            appSettingsResourceItem = assembly.GetManifestResourceStream("Plutus.Frontend.ClientUI.Configuration.appsettings.json");
#else
#if WINDOWS
            appSettingsResourceItem = assembly.GetManifestResourceStream("Plutus.Frontend.ClientUI.Configuration.appsettings.dev.json");
#elif ANDROID
            appSettingsResourceItem = assembly.GetManifestResourceStream("Plutus.Frontend.ClientUI.Configuration.appsettings.dev.Android.json");
#elif IOS
            appSettingsResourceItem = assembly.GetManifestResourceStream("Plutus.Frontend.ClientUI.Configuration.appsettings.dev.iOS.json");
#endif
#endif

            if (appSettingsResourceItem == default)
                throw new FileNotFoundException("AppSettings configuration file not found!");

            var config = new ConfigurationBuilder()
                         .AddJsonStream(appSettingsResourceItem)
                         .Build();

            builder.Configuration.AddConfiguration(config);
            appSettingsResourceItem.Close();
            return builder;
        }
    }
}
