using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Plutus.Frontend.ClientUI.Core;
using System;
using System.Collections.Generic;


/* Unmerged change from project 'Plutus.Frontend.ClientUI (net6.0-maccatalyst)'
Before:
[assembly: Dependency(typeof(Plutus.Frontend.ClientUI.Services.Analytics.AppState))]
After:
[assembly: Dependency(typeof(AppState))]
*/
[assembly: Dependency(typeof(Plutus.Frontend.ClientUI.Core.AppState))]
namespace Plutus.Frontend.ClientUI.Services.Analytics
{
    public class Logger : ILogger
    {
        private IAppState _appState;

        public Logger(IAppState appState)
        {
            _appState = appState;
        }

        public void LogEvent(AppLogLevel level, string message)
        {
            if (_appState.AppLogLevel <= level && !IsOnTestCloud() && !IsEmulatorOrSimulator())
            {
                Microsoft.AppCenter.Analytics.Analytics.TrackEvent($"{level}: {message}");
            }
        }

        public void LogEvent(AppLogLevel level, string message, Dictionary<string, string> dictionary)
        {
            if (_appState.AppLogLevel <= level && !IsOnTestCloud() && !IsEmulatorOrSimulator())
            {
                dictionary.Add("InstallId", _appState.InstallId.ToString());
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
