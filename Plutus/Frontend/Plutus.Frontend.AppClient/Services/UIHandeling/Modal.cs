using System;
using System.Threading;
using System.Threading.Tasks;

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

        /// <summary>Show something modal, one at a time.</summary>
        internal static async Task<T> ShowAsync<T>(Func<Task<T>> show)
        {
            if (show is null) throw new ArgumentNullException(nameof(show));

            // ⚠ Already inside a modal on this flow — the outer call is holding the gate and will do
            // the settle. Waiting here would be waiting on ourselves.
            if (Holding.Value) return await show().ConfigureAwait(true);

            await Gate.WaitAsync().ConfigureAwait(true);
            Holding.Value = true;
            try
            {
                return await show().ConfigureAwait(true);
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
