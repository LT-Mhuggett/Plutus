using Microsoft.Maui;

namespace Plutus.Frontend.AppClient.Tests
{
    /// <summary>
    /// A minimal IPlatformApplication so AppServices.Get&lt;T&gt;() (which reads
    /// IPlatformApplication.Current!.Services) works in tests without needing the real MAUI app host
    /// bootstrap - which fails anyway, since MauiProgram.CreateMauiApp() constructs App, a
    /// BindableObject that needs a live WinUI3 dispatcher.
    /// </summary>
    public class TestPlatformApplication : IPlatformApplication
    {
        public TestPlatformApplication(IServiceProvider services)
        {
            Services = services;
        }

        public IServiceProvider Services { get; }
        public IApplication Application => throw new NotSupportedException();
    }
}
