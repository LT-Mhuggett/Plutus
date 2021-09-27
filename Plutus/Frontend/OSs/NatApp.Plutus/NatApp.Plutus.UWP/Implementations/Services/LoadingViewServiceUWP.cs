using NatApp.Plutus.Services.Loading;
using NatApp.Plutus.UWP.Implementations.Services;
using NatApp.Plutus.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Xamarin.Forms;
using Xamarin.Forms.Platform.UWP;

[assembly: Dependency(typeof(LoadingViewServiceUWP))]
namespace NatApp.Plutus.UWP.Implementations.Services
{
    class LoadingViewServiceUWP : ILoadingViewService
    {
        private FrameworkElement _nativeView;

        private Popup _popup;

        private bool _isInitalized;

        public void InitLoadingPage(ContentPage loadingIndicatorView)
        {
            loadingIndicatorView.Parent = Xamarin.Forms.Application.Current.MainPage;
            loadingIndicatorView.Layout(new Rectangle(0, 0, Xamarin.Forms.Application.Current.MainPage.Width, Xamarin.Forms.Application.Current.MainPage.Height));

            var renderer = Platform.GetRenderer(loadingIndicatorView);
            if(renderer == null)
            {
                renderer = Platform.CreateRenderer(loadingIndicatorView);
                Platform.SetRenderer(loadingIndicatorView, renderer);
            }

            _nativeView = renderer.ContainerElement;

            _popup = new Popup();
            _popup.Child = _nativeView;

            _isInitalized = true;
        }

        public void ShowLoadingPage()
        {
            if (!_isInitalized)
                InitLoadingPage(new LoadingIndicatorView(Plutus.App.GetViewModel()));
            _popup.IsOpen = true;
        }

        public void HideLoadingPage()
        {
            _popup.IsOpen = false;
        }
    }
}
