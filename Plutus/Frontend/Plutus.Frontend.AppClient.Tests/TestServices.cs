using Plutus.Frontend.AppClient.Services.Analytics;
using Plutus.Frontend.AppClient.Services.IOHandeling;
using Plutus.Frontend.AppClient.Services.IOHandeling.Picker;
using Plutus.Frontend.AppClient.Services.Loading;
using Plutus.Frontend.AppClient.Services.POSHandeling;
using Plutus.Frontend.AppClient.Services.UIHandeling;

namespace Plutus.Frontend.AppClient.Tests
{
    /// <summary>
    /// Mutable slots the DI container (see TestBootstrap) resolves AppServices.Get&lt;T&gt;() calls
    /// through. Tests assign a Moq mock (or a hand-written fake) to the relevant property before
    /// exercising code that calls AppServices.Get&lt;T&gt;() for that interface. Since test
    /// parallelization is disabled assembly-wide (see TestBootstrap), tests don't race on these.
    /// Reset in each test's Arrange step rather than relying on a previous test's leftover value.
    /// </summary>
    public static class TestServices
    {
        public static IAppState? AppState { get; set; }
        public static Plutus.Frontend.AppClient.Services.Analytics.ILogger? Logger { get; set; }
        public static Plutus.Frontend.AppClient.Services.UIHandeling.IText? Text { get; set; }
        public static IFile? File { get; set; }
        public static IFolderPicker? FolderPicker { get; set; }
        public static ILoadingViewService? LoadingViewService { get; set; }
        public static IPOSCommunication? POSCommunication { get; set; }
    }
}
