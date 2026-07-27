using Microsoft.Maui.Controls.Compatibility.Platform.UWP;
using Microsoft.Maui.Graphics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Plutus.Frontend.ClientUI.Pages;
using Platform = Microsoft.Maui.Controls.Compatibility.Platform.UWP.Platform;

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

            var renderer = Platform.GetRenderer(LoadingIndicatorPage);
            if (renderer == null)
            {
                renderer = Platform.CreateRenderer(LoadingIndicatorPage);
                Platform.SetRenderer(LoadingIndicatorPage, renderer);
            }

            _nativeView = renderer.ContainerElement;

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
