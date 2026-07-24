using Plutus.Frontend.ClientUI.Pages;
using UIKit;
using Platform = Microsoft.Maui.Controls.Compatibility.Platform.iOS.Platform;

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

                var renderer = Platform.GetRenderer(LoadingIndicatorPage);
                if (renderer == null)
                {
                    renderer = Platform.CreateRenderer(LoadingIndicatorPage);
                    Platform.SetRenderer(LoadingIndicatorPage, renderer);
                }

                _nativeView = renderer.NativeView;

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
