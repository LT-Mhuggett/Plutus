using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;

namespace Plutus.Frontend.AppClient.Helpers.Compatibility
{
    /// <summary>
    /// Drop-in replacement for the Xamarin.Forms DependencyService.Get&lt;T&gt;() call sites. Named
    /// AppServices rather than DependencyService because Microsoft.Maui.Controls still ships its own
    /// (mostly vestigial) DependencyService type, which would otherwise collide with this one on every
    /// file that also needs `using Microsoft.Maui.Controls;` - and, more importantly, that built-in type
    /// resolves against its own internal registry (populated via DependencyService.Register/[assembly:
    /// Dependency]), not the MauiProgram.cs service container these platform services are registered in.
    /// </summary>
    public static class AppServices
    {
        public static T Get<T>() where T : class
            => IPlatformApplication.Current!.Services.GetRequiredService<T>();
    }
}
