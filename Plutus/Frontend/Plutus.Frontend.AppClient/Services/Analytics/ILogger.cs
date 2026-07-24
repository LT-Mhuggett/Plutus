using System;
using System.Collections.Generic;

namespace Plutus.Frontend.AppClient.Services.Analytics
{
    public interface ILogger
    {
        void LogEvent(AppLogLevel level, string message);
        void LogEvent(AppLogLevel level, string message, Dictionary<string, string> dictionary);
        void LogError(Exception ex);
        void LogError(Exception ex, Dictionary<string, string> properties);
    }
}
