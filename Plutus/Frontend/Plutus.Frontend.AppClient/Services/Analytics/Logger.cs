using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using OpenTelemetry.Trace;
using Plutus.Frontend.AppClient.Helpers.Compatibility;

namespace Plutus.Frontend.AppClient.Services.Analytics
{
    public class Logger : ILogger
    {
        private readonly IAppState _appState;
        private readonly Microsoft.Extensions.Logging.ILogger _melLogger;

        public Logger()
        {
            _appState = AppServices.Get<IAppState>();
            _melLogger = Observability.LoggerFactory.CreateLogger("Plutus");
        }

        public void LogEvent(AppLogLevel level, string message)
        {
            if (!ShouldLog(level))
                return;

            _melLogger.Log(Map(level), message);
        }

        public void LogEvent(AppLogLevel level, string message, Dictionary<string, string> dictionary)
        {
            if (!ShouldLog(level))
                return;

            dictionary["InstallId"] = _appState.GetInstallId().ToString();
            var attributes = dictionary.Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value)).ToList();
            _melLogger.Log(Map(level), new Microsoft.Extensions.Logging.EventId(0), attributes, null, (state, ex) => message);
        }

        public void LogError(Exception ex)
        {
            if (IsOnTestCloud() || IsEmulatorOrSimulator())
                return;

            Activity.Current?.RecordException(ex);
            Activity.Current?.SetStatus(ActivityStatusCode.Error);
            _melLogger.Log(Microsoft.Extensions.Logging.LogLevel.Error, ex, ex.Message);
        }

        public void LogError(Exception ex, Dictionary<string, string> properties)
        {
            if (IsOnTestCloud() || IsEmulatorOrSimulator())
                return;

            Activity.Current?.RecordException(ex);
            Activity.Current?.SetStatus(ActivityStatusCode.Error);
            var attributes = properties.Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value)).ToList();
            _melLogger.Log(Microsoft.Extensions.Logging.LogLevel.Error, new Microsoft.Extensions.Logging.EventId(0), attributes, ex, (state, e) => ex.Message);
        }

        private bool ShouldLog(AppLogLevel level) =>
            _appState.GetAppLogLevel() <= level && !IsOnTestCloud() && !IsEmulatorOrSimulator();

        private static Microsoft.Extensions.Logging.LogLevel Map(AppLogLevel level) => level switch
        {
            AppLogLevel.Verbose => Microsoft.Extensions.Logging.LogLevel.Trace,
            AppLogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
            AppLogLevel.Info => Microsoft.Extensions.Logging.LogLevel.Information,
            AppLogLevel.Warn => Microsoft.Extensions.Logging.LogLevel.Warning,
            AppLogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
            AppLogLevel.Fatal => Microsoft.Extensions.Logging.LogLevel.Critical,
            _ => Microsoft.Extensions.Logging.LogLevel.None,
        };

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
