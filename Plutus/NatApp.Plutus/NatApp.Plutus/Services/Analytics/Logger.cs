using System;
using System.Collections.Generic;
using Xamarin.Essentials;
using Xamarin.Forms;

[assembly: Xamarin.Forms.Dependency(typeof(NatApp.Plutus.Services.Analytics.AppState))]
namespace NatApp.Plutus.Services.Analytics
{
    public class Logger : ILogger
    {
        private IAppState _appState;

        public Logger()
        {
            _appState = DependencyService.Get<IAppState>();
        }

        public void LogEvent(AppLogLevel level, string message)
        {
            if (_appState.GetAppLogLevel() <= level && !IsOnTestCloud() && !IsEmulatorOrSimulator())
            {
                Microsoft.AppCenter.Analytics.Analytics.TrackEvent($"{level}: {message}");
            }
        }

        public void LogEvent(AppLogLevel level, string message, Dictionary<string, string> dictionary)
        {
            if (_appState.GetAppLogLevel() <= level && !IsOnTestCloud() && !IsEmulatorOrSimulator())
            {
                dictionary.Add("InstallId", _appState.GetInstallId().ToString());
                Microsoft.AppCenter.Analytics.Analytics.TrackEvent($"{level}: {message}", dictionary);
            }
        }

        private bool IsEmulatorOrSimulator()
        {
            return DeviceInfo.DeviceType == DeviceType.Virtual;
        }

        private bool IsOnTestCloud()
        {
            var isInTestCloud = Environment.GetEnvironmentVariable("XAMARIN_TEST_CLOUD");
            return isInTestCloud != null && isInTestCloud.Equals("1");
        }
    }
}
