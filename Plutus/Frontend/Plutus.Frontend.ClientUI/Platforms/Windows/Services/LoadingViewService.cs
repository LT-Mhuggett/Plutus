using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Plutus.Frontend.ClientUI.Pages;

namespace Plutus.Frontend.ClientUI.Services.Loading
{
    public partial class LoadingViewService
    {
        private FrameworkElement _nativeView;
        private Popup _popup;
        private bool _isInitialised;

        public partial void InitLoadingView()
        {
            LoadingIndicatorPage.Parent = App.Current.MainPage;
            LoadingIndicatorPage.Layout(new Rect(0, 0, App.Current.MainPage.Width, App.Current.MainPage.Height));

            // Handler-based replacement for the removed Compatibility renderer API
            // (Platform.GetRenderer/CreateRenderer/SetRenderer) - ToPlatform()
            // creates (or reuses) the page's native view via its MAUI handler.
            var mauiContext = App.Current.Windows[0].Handler?.MauiContext
                ?? throw new InvalidOperationException("No MauiContext available to render the loading view.");
            _nativeView = (FrameworkElement)LoadingIndicatorPage.ToPlatform(mauiContext);

            _popup = new Popup();
            _popup.Child = _nativeView;

            _isInitialised = true;
        }

        public partial void ShowLoadingView()
        {
            if (!_isInitialised)
                InitLoadingView();
            _popup.IsOpen = true;
        }

        public partial void HideLoadingView()
        {
            _popup.IsOpen = false;
        }
    }
}
