using System.Collections.Generic;

namespace Plutus.Frontend.ClientUI.Services.Analytics
{
    public interface ILogger
    {
        void LogEvent(AppLogLevel level, string message);
        void LogEvent(AppLogLevel level, string message, Dictionary<string, string> dictionary);
    }
}
