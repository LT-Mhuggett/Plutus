using Microsoft.Maui.Platform;
using Plutus.Frontend.ClientUI.Pages;
using DroidApp = Android.App;
using DroidGraphics = Android.Graphics;
using DroidViews = Android.Views;

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

                // Handler-based replacement for the removed Compatibility renderer API
                // (Platform.GetRenderer/CreateRendererWithContext/SetRenderer) - ToPlatform()
                // creates (or reuses) the page's native view via its MAUI handler.
                var mauiContext = App.Current.Windows[0].Handler?.MauiContext
                    ?? throw new InvalidOperationException("No MauiContext available to render the loading view.");
                _nativeView = (DroidViews.View)LoadingIndicatorPage.ToPlatform(mauiContext);

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
