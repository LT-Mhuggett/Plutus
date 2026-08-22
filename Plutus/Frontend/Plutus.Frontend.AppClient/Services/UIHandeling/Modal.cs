using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace Plutus.Frontend.AppClient.Services.UIHandeling
{
    /// <summary>
    /// One modal at a time, with a beat between them.
    ///
    /// ⚠ THIS EXISTS BECAUSE THE TILL CLOSED MID-SALE, 2026-08-10. Entering `0` at the payment
    /// prompt is refused and the tender loop asks again — so a Mopup popup was dismissed and a
    /// `DisplayActionSheet` was raised in the same instant. WinUI threw building the new one:
    ///
    ///     System.Runtime.InteropServices.COMException
    ///        at Microsoft.UI.Xaml.Controls.UserControl..ctor()
    ///        at Microsoft.Maui.Controls.Platform.ActionSheetContent..ctor(…)
    ///        at Page.DisplayActionSheet(…)
    ///
    /// The dialog that was closing had not finished tearing down. Nothing about the till's logic was
    /// wrong — the loop did exactly the right thing — and the app still went away, because the
    /// caller was an `async void` command with no catch.
    ///
    /// ⚠ THE DELAY IS A PRAGMATIC FIX AND IS LABELLED AS ONE. MAUI exposes no "this dialog has
    /// finished closing" signal: `DisplayActionSheet` returns when the operator taps, and Mopup's
    /// `PopAsync` completes when its animation does, but the platform's own teardown continues after
    /// both. A short settle is the honest tool available. If MAUI ever surfaces a completion signal,
    /// replace the delay — do not remove it and hope.
    ///
    /// ⚠ It is INSIDE the gate on purpose: the point is to keep the NEXT dialog waiting, not to make
    /// this caller slower.
    ///
    /// ⚠⚠ AND IT IS RE-ENTRANT, BECAUSE THE ALTERNATIVE STOPPED THE TILL TAKING MONEY.
    /// 2026-08-13, on 1.48.0: `InputAlertHelper` gates internally, a caller wrapped one of its
    /// prompts in this gate as well, and the flow waited on a semaphore it was already holding.
    /// The tender sheet closed, the amount box never appeared, and because the deadlock sat inside
    /// the checkout's `try`, its `finally { IsBusy = false; }` never ran — so the scan box, which
    /// opens `if (IsBusy) return;`, silently stopped searching for the rest of the session, and
    /// every dialog behind this gate died with it. No exception, no log line, nothing to grep.
    ///
    /// The redundant wrap is gone, but a dialog helper that protects itself is the RIGHT design, and
    /// a caller that reasonably wraps it must not be able to hang the app. So a nested call now
    /// PASSES THROUGH: the outermost call owns the gate and owns the settle.
    ///
    /// ⚠⚠ AND IT MARSHALS ONTO THE UI THREAD, BECAUSE THAT IS WHAT CLOSED THE TILL ON 1.49.0.
    /// Matt, 2026-08-13, hand-test A4 (overpay by card): the refusal message crashed the process.
    ///
    ///     System.Runtime.InteropServices.COMException
    ///        at Microsoft.UI.Xaml.Controls.ContentDialog..ctor()
    ///        at Microsoft.Maui.Controls.Platform.AlertManager.AlertRequestHelper.OnAlertRequested(…)
    ///        at System.Threading.Tasks.Task.ThrowAsync(…)      ← rethrown on the POOL, unobservable
    ///
    /// `DisplayAlert` and `DisplayActionSheet` construct a WinUI `ContentDialog` **on the calling
    /// thread**, and a XAML object built off the UI thread throws. The caller was the pool, because
    /// `Client.Core.TenderLoop` awaits its callbacks with `ConfigureAwait(false)` — which is CORRECT
    /// for a shared library with no UI to return to. So the boundary has to marshal, and this is the
    /// boundary.
    ///
    /// ⚠ Why a successful sale did NOT crash, since that is the confusing part: `MopupService` marshals
    /// internally, so the amount prompt hops onto the UI thread and stays there — and the viewmodel
    /// awaits `TenderLoop.RunAsync` without `ConfigureAwait(false)`, so the happy path lands back on
    /// the UI thread before it shows anything else. Only the REFUSAL path raised a dialog while still
    /// on the pool, which is why a normal sale worked and overpaying by card killed the app.
    ///
    /// ⚠ The exception is delivered by `Task.ThrowAsync` on a pool thread, so a `catch` around the
    /// checkout could never have caught it. **Getting the thread right is the only fix; a try/catch is
    /// not an alternative.**
    /// </summary>
    internal static class Modal
    {
        /// <summary>Long enough for WinUI to finish disposing a dialog, short enough that nobody at
        /// a counter perceives it.</summary>
        private const int SettleMs = 120;

        private static readonly SemaphoreSlim Gate = new(1, 1);

        /// <summary>
        /// Set while this flow holds the gate. ⚠ `AsyncLocal` and not a plain `static bool`: the
        /// value must follow ONE logical flow across its awaits, and must not be visible to another.
        /// A static flag would let a modal raised from anywhere else skip the gate entirely, which
        /// is the COMException this class exists to prevent.
        /// </summary>
        private static readonly AsyncLocal<bool> Holding = new();

        /// <summary>
        /// Whether there is a UI thread to marshal onto at all.
        ///
        /// ⚠ Probed through `Application.Current`, and NOT by attempting the marshal and catching:
        /// `InvokeOnMainThreadAsync` cannot tell you whether it failed before or after invoking the
        /// delegate, so a retry-on-failure fallback could show the same dialog twice.
        ///
        /// ⚠ False in the unit-test host, which never constructs `App` — a `BindableObject` needs a
        /// live WinUI dispatcher that xunit does not have. There the delegate runs inline, which is
        /// correct: there is no UI thread to be wrong about.
        /// </summary>
        private static bool HasUiThread
        {
            get
            {
                try { return Application.Current?.Dispatcher is not null; }
                catch { return false; }
            }
        }

        /// <summary>Run the dialog where WinUI will accept it.</summary>
        private static Task<T> OnUiThread<T>(Func<Task<T>> show)
            => !HasUiThread || MainThread.IsMainThread
                ? show()
                : MainThread.InvokeOnMainThreadAsync(show);

        /// <summary>Show something modal, one at a time, on the UI thread.</summary>
        internal static async Task<T> ShowAsync<T>(Func<Task<T>> show)
        {
            if (show is null) throw new ArgumentNullException(nameof(show));

            // ⚠ Already inside a modal on this flow — the outer call is holding the gate and will do
            // the settle. Waiting here would be waiting on ourselves.
            if (Holding.Value) return await OnUiThread(show).ConfigureAwait(true);

            // ⚠⚠ A DEADLINE, AND THEN IT GOES AHEAD ANYWAY — the one gate in the till that does.
            //
            // This gate SERIALISES dialogs; it does not make them correct. If it is stuck, refusing
            // to wait any longer and showing the dialog risks two stacked modals, which is untidy.
            // Refusing to show it at all means the till can no longer ask the operator ANYTHING —
            // no confirm, no amount, no payment — and there is no way back from that without a
            // restart. Untidy beats unusable.
            //
            // ⚠ 30s, the same number as every other gate here. Anything past half a minute waiting
            // for a dialog slot is a hang, not contention: nothing legitimately holds this that long.
            if (!await Gate.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(true))
            {
                Analytics.CrashLog.Write("Modal.ShowAsync(gate-timeout)", new TimeoutException(
                    "A dialog held the modal gate for over 30 seconds. Showing this one anyway — "
                    + "a till that cannot ask the operator a question cannot take a payment."));
                return await OnUiThread(show).ConfigureAwait(true);
            }
            Holding.Value = true;
            try
            {
                return await OnUiThread(show).ConfigureAwait(true);
            }
            finally
            {
                // ⚠ Cleared before the release, and deliberately not relied upon: an `AsyncLocal`
                // write does not propagate back to our caller, so this only tidies our own flow.
                Holding.Value = false;

                // ⚠ Even if `show` threw. A dialog that failed to open still leaves the platform
                // mid-teardown, and the next one must not walk into it.
                try { await Task.Delay(SettleMs).ConfigureAwait(true); } catch { }
                Gate.Release();
            }
        }
    }
}
