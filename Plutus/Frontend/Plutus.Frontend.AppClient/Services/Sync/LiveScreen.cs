using System;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Plutus.Frontend.AppClient.Services.Analytics;

namespace Plutus.Frontend.AppClient.Services.Sync
{
    /// <summary>
    /// Keeps ONE screen's data current: reload when the operator looks at it, and again on every
    /// 60-second tick while they are still looking. Two lines in a page's constructor.
    ///
    /// ⚠⚠ **THIS EXISTS BECAUSE THE SAME FAULT WAS REPORTED THREE TIMES AND FIXED ONCE EACH TIME.**
    /// Matt, 2026-08-11: *"The open float was 'Waiting' and never updated. I navigated away and back
    /// onto the cash tab and it had updated."* Then finding N: today's takings were read at sign-in
    /// and a full day of trading never moved them. Then 2026-08-18, §5c item 7, as a general
    /// statement: *"Nothing updates unless you navigate away and back."* Each earlier fix was
    /// correct **and local**, so the next screen inherited nothing. Runbook pitfall 17 ends *"when
    /// you find one stale screen, go and look for its siblings straight away"* — and nobody did,
    /// which is why the third report was needed.
    ///
    /// ⚠ THE ROOT CAUSE IS `AppShell`: it builds every tab up front, so a viewmodel constructor runs
    /// ONCE, at sign-in. Anything loaded there is frozen for the life of the session. `OnAppearing`
    /// alone only half-fixes it — it makes *"navigate away and back"* work, which is precisely the
    /// workaround Matt was describing rather than the fix.
    ///
    /// ⚠ **THE DANGEROUS STALENESS IS THE SILENT KIND.** A stuck *"(waiting to send)"* gets reported
    /// within the hour. A takings total eight hours old **looks exactly like a correct one**, and it
    /// is the number a manager counts a drawer against.
    ///
    /// **Use it like this** — no `OnAppearing` override, and nothing to remember to undo:
    /// <code>
    /// private readonly Services.Sync.LiveScreen _live;
    /// public ThingView()
    /// {
    ///     InitializeComponent();
    ///     BindingContext = _vm = new ThingViewModel(...);
    ///     _live = new Services.Sync.LiveScreen(this, _vm.Refresh);
    /// }
    /// </code>
    ///
    /// ⚠ It hooks the page's PUBLIC `Appearing` / `Disappearing` events rather than the protected
    /// overrides, deliberately: a page keeps its own overrides for its own business, and there is no
    /// base call to forget. It also means adopting this needs **no XAML change** — the root element
    /// stays `ContentPage`, and on a UI framework whose bindings fail silently, not touching the
    /// XAML is worth a great deal.
    ///
    /// ⚠ NOT FOR MODAL FORMS. A dialog or an add/edit page has nothing to restate; reloading under
    /// somebody's hands mid-type is a fault, not a feature.
    ///
    /// ⚠⚠ **AND NOT EVERY SCREEN SHOULD TICK — pass `onCadence: false` for long tables.** A refresh
    /// that rebuilds an `ObservableCollection` sends a `CollectionView` back to the top, so a
    /// minute-by-minute reload of a 500-row item list would yank the page out from under somebody
    /// reading it. **That is a worse fault than the staleness it fixes**, and it would be one we
    /// introduced rather than inherited. The test is what the screen IS: a small live figure somebody
    /// watches (the drawer, today's takings, a read-only detail card) ticks; a long list they scroll,
    /// and re-query on purpose with a Search button, does not.
    /// </summary>
    public sealed class LiveScreen
    {
        private readonly Action _refresh;
        private readonly bool _onCadence;
        private bool _subscribed;

        /// <param name="page">The page to follow. Its `Appearing`/`Disappearing` keep this alive.</param>
        /// <param name="refresh">
        /// Reload this screen's data. ⚠ It is called on the UI thread (see <see cref="Run"/>), it may
        /// be called every 60 seconds, and **it must not throw** — though this catches anyway,
        /// because a background loop that takes the app down is worse than a stale figure.
        /// </param>
        /// <param name="onCadence">
        /// Reload every 60 seconds as well, not only on appearing. ⚠ `false` for a long scrollable
        /// table — see the class header. It still refreshes on appearing, which is what makes
        /// *"navigate away and back"* unnecessary.
        /// </param>
        public LiveScreen(Page page, Action refresh, bool onCadence = true)
        {
            if (page is null) throw new ArgumentNullException(nameof(page));
            _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
            _onCadence = onCadence;

            page.Appearing += OnAppearing;
            page.Disappearing += OnDisappearing;
        }

        /// <summary>
        /// ⚠ REFRESH FIRST, THEN SUBSCRIBE. A screen that only subscribed would show sign-in-era data
        /// for up to a minute — the exact window in which somebody reads it and walks away.
        ///
        /// ⚠ The `_subscribed` guard is not defensive noise: MAUI raises `Appearing` again when a
        /// modal over the page is dismissed, WITHOUT a matching `Disappearing`. Without the guard
        /// every dialog a screen shows would add another handler, and sixty seconds later they would
        /// all reload at once.
        /// </summary>
        private void OnAppearing(object sender, EventArgs e)
        {
            Run();

            if (!_onCadence || _subscribed) return;
            TillCadence.Ticked += OnTicked;
            _subscribed = true;
        }

        /// <summary>
        /// ⚠ UNSUBSCRIBE, ALWAYS. `TillCadence.Ticked` is a STATIC event, so a page that stays
        /// attached is held alive for the life of the process along with every query it makes each
        /// minute — on a till that runs for a fortnight without restarting.
        /// </summary>
        private void OnDisappearing(object sender, EventArgs e)
        {
            if (!_subscribed) return;
            TillCadence.Ticked -= OnTicked;
            _subscribed = false;
        }

        private void OnTicked() => Run();

        /// <summary>
        /// ⚠ MARSHALS, ALWAYS, so no screen has to think about it. `Ticked` arrives on the cadence
        /// loop's thread, and a refresh that writes an `ObservableCollection` a `CollectionView` is
        /// bound to must do it on the UI thread — off it, that is the fault class that passes every
        /// test and throws on a shop floor. A refresh that does its own work off-thread loses
        /// nothing by being *started* on the UI thread.
        ///
        /// ⚠ CATCHES ON BOTH SIDES OF THE HOP. An exception inside the marshalled lambda has no
        /// caller left to catch it — it goes to the dispatcher unhandled, which on MAUI kills the
        /// till. That is the same shape as every `async void` crash this app has had.
        /// </summary>
        private void Run()
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        _refresh();
                    }
                    catch (Exception ex)
                    {
                        CrashLog.Write("LiveScreen.refresh", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                CrashLog.Write("LiveScreen.marshal", ex);
            }
        }
    }
}
