using Microsoft.Maui.Controls.Compatibility.Platform.Android;
using Plutus.Frontend.ClientUI.Pages;
using DroidApp = Android.App;
using DroidGraphics = Android.Graphics;
using DroidViews = Android.Views;
using Platform = Microsoft.Maui.Controls.Compatibility.Platform.Android.Platform;

namespace Plutus.Frontend.ClientUI.Services.Loading
{
    public partial class LoadingViewService
    {
        private DroidViews.View _nativeView;
        private DroidApp.Dialog _dialog;
        private bool _isInitialised;

        public partial void InitLoadingView()
        {
            if (LoadingIndicatorPage == null)
            {
                LoadingIndicatorPage.Parent = App.Current.MainPage;
                LoadingIndicatorPage.Layout(new Microsoft.Maui.Graphics.Rect(0, 0, App.Current.MainPage.Width, App.Current.MainPage.Height));

                var renderer = Platform.GetRenderer(LoadingIndicatorPage);
                if (renderer == null)
                {
                    renderer = Platform.CreateRendererWithContext(LoadingIndicatorPage, DroidApp.Application.Context);
                    Platform.SetRenderer(LoadingIndicatorPage, renderer);
                }

                _nativeView = renderer.View;

                _dialog = new DroidApp.Dialog(DroidApp.Application.Context);

                _dialog.RequestWindowFeature((int)DroidViews.WindowFeatures.NoTitle);
                _dialog.SetCancelable(false);
                _dialog.SetContentView(_nativeView);
                DroidViews.Window window = _dialog.Window;
                window.SetLayout(DroidViews.ViewGroup.LayoutParams.MatchParent, DroidViews.ViewGroup.LayoutParams.MatchParent);
                window.ClearFlags(DroidViews.WindowManagerFlags.DimBehind);
                window.SetBackgroundDrawable(new DroidGraphics.Drawables.ColorDrawable(DroidGraphics.Color.Transparent));
                _isInitialised = true;
            }
        }

        public partial void ShowLoadingView()
        {
            if (!_isInitialised)
                InitLoadingView();
            _dialog.Show();
        }

        public partial void HideLoadingView()
        {
            _dialog.Hide();
        }
    }
}
