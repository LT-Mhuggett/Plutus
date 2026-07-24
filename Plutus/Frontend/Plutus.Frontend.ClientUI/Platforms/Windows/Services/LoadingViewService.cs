namespace Plutus.Frontend.ClientUI.Services.Loading
{
    public partial class LoadingViewService
    {
        // MIGRATION TODO (net7 Xamarin.Forms -> net10 MAUI):
        // The Xamarin.Forms UWP renderer API (Microsoft.Maui.Controls.Compatibility.Platform.UWP.Platform
        // GetRenderer/CreateRenderer/SetRenderer) was removed in .NET MAUI. The loading overlay must be
        // reimplemented using MAUI handlers (e.g. LoadingIndicatorPage.ToPlatform(mauiContext) hosted in a
        // WinUI Popup) or a CommunityToolkit overlay. Stubbed to no-ops so the app builds and runs for review.
        public partial void InitLoadingView() { }

        public partial void ShowLoadingView() { }

        public partial void HideLoadingView() { }
    }
}
