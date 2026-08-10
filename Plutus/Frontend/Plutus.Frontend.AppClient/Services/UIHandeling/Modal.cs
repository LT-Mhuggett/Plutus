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
    /// </summary>
    internal static class Modal
    {
        /// <summary>Long enough for WinUI to finish disposing a dialog, short enough that nobody at
        /// a counter perceives it.</summary>
        private const int SettleMs = 120;

        private static readonly SemaphoreSlim Gate = new(1, 1);

        /// <summary>Show something modal, one at a time.</summary>
        internal static async Task<T> ShowAsync<T>(Func<Task<T>> show)
        {
            if (show is null) throw new ArgumentNullException(nameof(show));

            await Gate.WaitAsync().ConfigureAwait(true);
            try
            {
                return await show().ConfigureAwait(true);
            }
            finally
            {
                // ⚠ Even if `show` threw. A dialog that failed to open still leaves the platform
                // mid-teardown, and the next one must not walk into it.
                try { await Task.Delay(SettleMs).ConfigureAwait(true); } catch { }
                Gate.Release();
            }
        }
    }
}
