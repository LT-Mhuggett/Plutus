using Microsoft.Maui.Controls;
using Plutus.Frontend.AppClient.Views;

namespace Plutus.Frontend.AppClient.Services.Loading
{
    /// <summary>
    /// Each platform head previously hand-rolled ILoadingViewService by grabbing the native renderer
    /// for LoadingIndicatorView and hosting it in a platform popup (Xamarin.Forms.Platform.*.Platform
    /// .CreateRenderer, which has no MAUI equivalent - renderers were replaced by handlers). Since
    /// LoadingIndicatorView is just a translucent full-screen ContentPage, a modal push/pop achieves
    /// the same show/hide overlay behavior identically on every platform, so one shared implementation
    /// now covers what used to be three separate native ones.
    /// </summary>
    public class LoadingViewService : ILoadingViewService
    {
        private LoadingIndicatorView _loadingIndicatorView;
        private bool _isShowing;

        public void InitLoadingPage(ContentPage loadingIndicatorView)
        {
            _loadingIndicatorView = loadingIndicatorView as LoadingIndicatorView;
        }

        public async void ShowLoadingPage()
        {
            if (_isShowing)
                return;

            _loadingIndicatorView ??= new LoadingIndicatorView(App.GetViewModel());
            _isShowing = true;
            await Application.Current!.MainPage!.Navigation.PushModalAsync(_loadingIndicatorView, false);
        }

        public async void HideLoadingPage()
        {
            if (!_isShowing)
                return;

            _isShowing = false;
            await Application.Current!.MainPage!.Navigation.PopModalAsync(false);
        }
    }
}
