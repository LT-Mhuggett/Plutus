using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
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
    ///
    /// ⚠ THE SEQUENCING LIVES IN <see cref="OverlayGate"/>, and the reason is written up there: the
    /// straight-line version of this class strands the overlay permanently the first time a screen
    /// loads faster than a modal push, which bricks every screen after it. This class is now only
    /// the MAUI half — push, pop, and getting onto the UI thread.
    /// </summary>
    public class LoadingViewService : ILoadingViewService
    {
        private LoadingIndicatorView _loadingIndicatorView;
        private readonly OverlayGate _gate;

        public LoadingViewService()
        {
            _gate = new OverlayGate(
                show: PushAsync,
                hide: PopAsync,
                onError: ex => Analytics.CrashLog.Write("LoadingViewService", ex));
        }

        public void InitLoadingPage(ContentPage loadingIndicatorView)
        {
            _loadingIndicatorView = loadingIndicatorView as LoadingIndicatorView;
        }

        public void ShowLoadingPage() => Request(true);

        public void HideLoadingPage() => Request(false);

        /// <summary>
        /// ⚠ Marshalled, because <see cref="OverlayGate"/> uses unsynchronised fields and is only
        /// safe if every call arrives on one thread. Callers reach here from background work often
        /// enough — a finished load clearing its own spinner — that requiring them to marshal first
        /// would be a rule broken silently.
        /// </summary>
        private void Request(bool visible)
        {
            if (MainThread.IsMainThread) _ = _gate.RequestAsync(visible);
            else MainThread.BeginInvokeOnMainThread(() => _ = _gate.RequestAsync(visible));
        }

        private async Task PushAsync()
        {
            var nav = Application.Current?.MainPage?.Navigation;
            if (nav is null) return;

            _loadingIndicatorView ??= new LoadingIndicatorView(App.GetViewModel());
            await nav.PushModalAsync(_loadingIndicatorView, false);
        }

        /// <summary>
        /// ⚠ Pops ONLY when our own page is on top. `PopModalAsync` takes no argument — it removes
        /// whatever is topmost — so popping blind would close somebody else's page and leave the
        /// overlay behind, which is both halves of the bug at once.
        /// </summary>
        private async Task PopAsync()
        {
            var nav = Application.Current?.MainPage?.Navigation;
            if (nav is null) return;

            var stack = nav.ModalStack;
            if (stack.Count == 0) return;
            if (!ReferenceEquals(stack[stack.Count - 1], _loadingIndicatorView)) return;

            await nav.PopModalAsync(false);
        }
    }
}
