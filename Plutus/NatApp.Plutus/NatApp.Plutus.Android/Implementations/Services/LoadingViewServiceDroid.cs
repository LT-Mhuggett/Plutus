using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;

using Xamarin.Forms;

using NatApp.Plutus.Droid.Implementations.Services;
using NatApp.Plutus.Services.Loading;
using Xamarin.Forms.Platform.Android;
using Android.Graphics.Drawables;
using NatApp.Plutus.Views;
using Plugin.CurrentActivity;

[assembly: Dependency(typeof(LoadingViewServiceDroid))]
namespace NatApp.Plutus.Droid.Implementations.Services
{
    class LoadingViewServiceDroid : ILoadingViewService
    {
        private Android.Views.View _nativeView;

        private Dialog _dialog;

        private bool _isInitalized;

        public void InitLoadingPage(ContentPage loadingIndicatorView)
        {
            loadingIndicatorView.Parent = Xamarin.Forms.Application.Current.MainPage;
            loadingIndicatorView.Layout(new Rectangle(0, 0, Xamarin.Forms.Application.Current.MainPage.Width, Xamarin.Forms.Application.Current.MainPage.Height));

            var renderer = Platform.GetRenderer(loadingIndicatorView);
            if (renderer == null)
            {
                renderer = Platform.CreateRendererWithContext(loadingIndicatorView, CrossCurrentActivity.Current.Activity);
                Platform.SetRenderer(loadingIndicatorView, renderer);
            }

            _nativeView = renderer.View;

            _dialog = new Dialog(CrossCurrentActivity.Current.Activity);

            _dialog.RequestWindowFeature((int)WindowFeatures.NoTitle);
            _dialog.SetCancelable(false);
            _dialog.SetContentView(_nativeView);
            Window window = _dialog.Window;
            window.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
            window.ClearFlags(WindowManagerFlags.DimBehind);
            window.SetBackgroundDrawable(new ColorDrawable(Android.Graphics.Color.Transparent));
            _isInitalized = true;
        }
        

        public void ShowLoadingPage()
        {
            if (!_isInitalized)
                InitLoadingPage(new LoadingIndicatorView(App.GetViewModel()));
            _dialog.Show();
        }

        public void HideLoadingPage()
        {
            _dialog.Hide();
        }
    }
}