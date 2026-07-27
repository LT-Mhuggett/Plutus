using Microsoft.Maui.Platform;
using Plutus.Frontend.ClientUI.Pages;
using UIKit;

namespace Plutus.Frontend.ClientUI.Services.Loading
{
    public partial class LoadingViewService
    {
        private UIView _nativeView;
        private bool _isInitialized;

        public partial void InitLoadingView()
        {
            if (LoadingIndicatorPage != null)
            {
                LoadingIndicatorPage.Parent = Application.Current.MainPage;
                LoadingIndicatorPage.Layout(new Microsoft.Maui.Graphics.Rect(0, 0, Application.Current.MainPage.Width, Application.Current.MainPage.Height));

                // Handler-based replacement for the removed Compatibility renderer API
                // (Platform.GetRenderer/CreateRenderer/SetRenderer) - ToPlatform() creates (or
                // reuses) the page's native view via its MAUI handler.
                var mauiContext = Application.Current.Windows[0].Handler?.MauiContext
                    ?? throw new InvalidOperationException("No MauiContext available to render the loading view.");
                _nativeView = (UIView)LoadingIndicatorPage.ToPlatform(mauiContext);

                _isInitialized = true;
            }
        }

        public partial void ShowLoadingView()
        {
            if (!_isInitialized)
                InitLoadingView();
            UIApplication.SharedApplication.KeyWindow.AddSubview(_nativeView);
        }

        public partial void HideLoadingView()
        {
            _nativeView.RemoveFromSuperview();
        }
    }
}
