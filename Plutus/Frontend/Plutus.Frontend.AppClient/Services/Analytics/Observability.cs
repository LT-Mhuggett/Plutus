using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry.Trace;

namespace Plutus.Frontend.AppClient.Services.Analytics
{
    /// <summary>
    /// Holds the real ILoggerFactory/TracerProvider once MauiProgram.CreateMauiApp() has built them.
    /// Defaults to no-op implementations so any code that resolves this before Bootstrap() runs (e.g.
    /// the xUnit test process, which never calls CreateMauiApp()) gets safe no-ops instead of a null ref.
    /// ActivitySource is always constructible and safe to use standalone - StartActivity() simply
    /// returns null when no TracerProvider is listening, which every call site null-conditions on.
    /// </summary>
    internal static class Observability
    {
        internal static ILoggerFactory LoggerFactory { get; private set; } = NullLoggerFactory.Instance;

        internal static ActivitySource ActivitySource { get; } = new("Plutus");

        internal static TracerProvider? TracerProvider { get; private set; }

        internal static void Bootstrap(ILoggerFactory loggerFactory, TracerProvider? tracerProvider = null)
        {
            LoggerFactory = loggerFactory;
            TracerProvider = tracerProvider;
        }
    }
}
