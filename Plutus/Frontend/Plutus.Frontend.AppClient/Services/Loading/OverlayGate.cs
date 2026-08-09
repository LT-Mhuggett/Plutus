using System;
using System.Threading.Tasks;

namespace Plutus.Frontend.AppClient.Services.Loading
{
    /// <summary>
    /// Decides when the loading overlay is actually pushed and popped. Deliberately knows NOTHING
    /// about MAUI — it takes a show and a hide delegate — because the bug it exists to prevent is a
    /// timing bug, and a timing bug you cannot write a test for is a timing bug you will ship twice.
    ///
    /// ⚠ THE NAIVE VERSION PERMANENTLY BRICKS THE APP, and did. The original service was:
    ///
    ///     public async void ShowLoadingPage()
    ///     {
    ///         if (_isShowing) return;
    ///         _isShowing = true;                       // flipped BEFORE the push finishes
    ///         await ...PushModalAsync(_view, false);
    ///     }
    ///     public async void HideLoadingPage()
    ///     {
    ///         if (!_isShowing) return;
    ///         _isShowing = false;
    ///         await ...PopModalAsync(false);
    ///     }
    ///
    /// A screen that loads FAST — a local SQLite read, which is most of them — asks to hide while
    /// the push is still in flight. The hide sees `_isShowing == true`, sets it false and pops a
    /// modal stack the overlay has not landed on yet. Then the push completes and puts the overlay
    /// up, with `_isShowing` already false, so every later hide returns at its first line. The
    /// overlay stays over the app FOR THE REST OF THE PROCESS, and every subsequent screen reads as
    /// "hung" — which is exactly how it was reported, on two different screens, neither of which
    /// was at fault.
    ///
    /// ⚠ So the rule is: NEVER act on the request, act on the DIFFERENCE between what was asked for
    /// and what is really on screen, one operation at a time, each awaited to completion. A request
    /// that arrives mid-flight only moves the target; the loop re-reads it after the await and
    /// settles there.
    ///
    /// ⚠ SINGLE-THREADED BY CONTRACT — every call must come from the UI thread (see
    /// <see cref="LoadingViewService"/>, which marshals). That is what makes the plain `bool` fields
    /// safe: nothing can run between the loop's final check and clearing <c>_running</c>, so a
    /// request can never be dropped in the gap.
    /// </summary>
    public sealed class OverlayGate
    {
        private readonly Func<Task> _show;
        private readonly Func<Task> _hide;
        private readonly Action<Exception> _onError;

        private bool _wanted;
        private bool _shown;
        private bool _running;
        private Task _loop = Task.CompletedTask;

        public OverlayGate(Func<Task> show, Func<Task> hide, Action<Exception> onError = null)
        {
            _show = show ?? throw new ArgumentNullException(nameof(show));
            _hide = hide ?? throw new ArgumentNullException(nameof(hide));
            _onError = onError;
        }

        /// <summary>Whether the overlay is believed to be on screen right now.</summary>
        public bool IsShown => _shown;

        /// <summary>
        /// Ask for the overlay to be up or down. The returned task completes when the gate has
        /// SETTLED — not when this particular request was applied — which is the only thing a
        /// caller could sensibly wait for, and what the tests assert on.
        /// </summary>
        public Task RequestAsync(bool visible)
        {
            _wanted = visible;
            if (_running) return _loop;   // the in-flight loop will pick the new target up
            return _loop = ReconcileAsync();
        }

        private async Task ReconcileAsync()
        {
            _running = true;
            try
            {
                while (_wanted != _shown)
                {
                    // ⚠ Re-read every pass. This is the whole point: the target may have moved
                    // while the previous push or pop was in flight.
                    if (_wanted)
                    {
                        await _show().ConfigureAwait(true);
                        _shown = true;
                    }
                    else
                    {
                        await _hide().ConfigureAwait(true);
                        _shown = false;
                    }
                }
            }
            catch (Exception ex)
            {
                _onError?.Invoke(ex);

                // ⚠ FAIL OPEN, ALWAYS. If we cannot tell what is on screen, assume the overlay is
                // down and stop wanting it up. Being wrong here shows a spinner that a later hide
                // can clear; the alternative — assuming it is still up — is the locked door this
                // class exists to prevent, and no user can get out of that one.
                _shown = false;
                _wanted = false;
            }
            finally
            {
                _running = false;
            }
        }
    }
}
