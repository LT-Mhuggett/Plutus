using System.Runtime.CompilerServices;
using I18N_L10N.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Plutus.Frontend.AppClient.Services.Analytics;
using Plutus.Frontend.AppClient.Services.IOHandeling;
using Plutus.Frontend.AppClient.Services.IOHandeling.Picker;
using Plutus.Frontend.AppClient.Services.Loading;
using Plutus.Frontend.AppClient.Services.POSHandeling;
using Plutus.Frontend.AppClient.Services.ThirdPartyTransfer;
using Plutus.Frontend.AppClient.Services.UIHandeling;

// App.TranslateExtension (which every ".Translate()" call reads) is one shared instance whose Text
// property is mutated as a side effect of ProvideValue(). xUnit parallelizes test classes by default,
// so two tests calling .Translate() concurrently can read back each other's Key/exception message.
// This mirrors a real, pre-existing thread-safety gap in TranslateExtension itself (out of scope to
// fix here), but the test suite needs to run sequentially to avoid flaking on it - the same reason
// TestServices' mutable slots (below) are safe to share across the assembly.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Plutus.Frontend.AppClient.Tests
{
    /// <summary>
    /// Most production code calls the ".Translate()" string extension, which reads the static
    /// App.TranslateExtension field the real app's constructor sets up, and AppServices.Get&lt;T&gt;()
    /// (the DependencyService replacement), which reads IPlatformApplication.Current!.Services. Tests
    /// never construct a real App or run MauiProgram.CreateMauiApp() (it constructs App, a
    /// BindableObject that needs a live WinUI3 dispatcher unavailable here - see
    /// ValidationGroupBehaviorTests for the same underlying constraint), so this sets both up directly:
    /// a real TranslateExtension, and a minimal IPlatformApplication whose DI container resolves each
    /// service interface through TestServices' mutable, per-test-settable slots.
    ///
    /// Every registration below is AddTransient, not AddSingleton: a singleton registration resolves
    /// its factory once and caches that instance for the container's lifetime - which is the whole
    /// test assembly here, since this runs once via ModuleInitializer - so the very first test to
    /// touch a given interface would "lock in" its mock for every later test. Transient re-reads the
    /// current TestServices.* value on every call, which is what lets each test set its own mock.
    /// </summary>
    internal static class TestBootstrap
    {
        [ModuleInitializer]
        internal static void Init()
        {
            App.TranslateExtension = new TranslateExtension();

            var services = new ServiceCollection();
            services.AddTransient(_ => TestServices.AppState ?? throw new InvalidOperationException("Set TestServices.AppState before resolving IAppState."));
            services.AddTransient(_ => TestServices.Logger ?? throw new InvalidOperationException("Set TestServices.Logger before resolving ILogger."));
            services.AddTransient(_ => TestServices.Text ?? throw new InvalidOperationException("Set TestServices.Text before resolving IText."));
            services.AddTransient(_ => TestServices.File ?? throw new InvalidOperationException("Set TestServices.File before resolving IFile."));
            services.AddTransient(_ => TestServices.FolderPicker ?? throw new InvalidOperationException("Set TestServices.FolderPicker before resolving IFolderPicker."));
            services.AddTransient(_ => TestServices.LoadingViewService ?? throw new InvalidOperationException("Set TestServices.LoadingViewService before resolving ILoadingViewService."));
            services.AddTransient(_ => TestServices.POSCommunication ?? throw new InvalidOperationException("Set TestServices.POSCommunication before resolving IPOSCommunication."));

            IPlatformApplication.Current = new TestPlatformApplication(services.BuildServiceProvider());
        }
    }
}
