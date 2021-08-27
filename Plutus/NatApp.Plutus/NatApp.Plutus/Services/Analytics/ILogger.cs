using System.Collections.Generic;

namespace NatApp.Plutus.Services.Analytics
{
    public interface ILogger
    {
        void LogEvent(AppLogLevel level, string message);
        void LogEvent(AppLogLevel level, string message, Dictionary<string, string> dictionary);
    }
}
