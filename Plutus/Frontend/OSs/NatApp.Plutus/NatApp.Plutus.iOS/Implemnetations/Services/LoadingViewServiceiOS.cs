using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NatApp.Plutus.iOS.Implemnetations.Services;
using NatApp.Plutus.Services.Loading;
using NatApp.Plutus.Views;
using UIKit;
using Xamarin.Forms;
using Xamarin.Forms.Platform.iOS;

[assembly: Dependency(typeof(LoadingViewServiceiOS))]
namespace NatApp.Plutus.iOS.Implemnetations.Services
{
    class LoadingViewServiceiOS : ILoadingViewService
    {
        private UIView _nativeView;

        private bool _isInitalized;

        public void InitLoadingPage(ContentPage loadingIndicatorView)
        {
            loadingIndicatorView.Parent = Xamarin.Forms.Application.Current.MainPage;
            loadingIndicatorView.Layout(new Rectangle(0, 0, Xamarin.Forms.Application.Current.MainPage.Width, Xamarin.Forms.Application.Current.MainPage.Height));

            var renderer = Platform.GetRenderer(loadingIndicatorView);
            if (renderer == null)
            {
                renderer = Platform.CreateRenderer(loadingIndicatorView);
                Platform.SetRenderer(loadingIndicatorView, renderer);
            }

            _nativeView = renderer.NativeView;

            _isInitalized = true;
        }
        public void ShowLoadingPage()
        {
            if (!_isInitalized)
                InitLoadingPage(new LoadingIndicatorView());
            UIApplication.SharedApplication.KeyWindow.AddSubview(_nativeView);
        }

        public void HideLoadingPage()
        {
            _nativeView.RemoveFromSuperview();
        }
    }
}