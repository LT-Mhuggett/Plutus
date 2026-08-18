using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CustomViews;
using Mopups.Services;
using Plutus.Frontend.AppClient.Views.CustomViews;

namespace Plutus.Frontend.AppClient.Helpers.CustomViews
{
    /// <summary>
    /// Show one customer's detail view (WP-L1, §5d).
    ///
    /// ⚠ ONE PUSH, ONE AWAIT, ONE POP **IN A `finally`** — the rule `InputAlertHelper.ShowAsync`'s
    /// header records the hard way. An un-popped popup is a 40%-black sheet over a till nobody can
    /// dismiss, mid-shift.
    ///
    /// ⚠⚠ THE ACTION CALLBACKS CLOSE THIS DIALOG FIRST. Edit and Grant credit each raise their own
    /// `InputAlert`, and MAUI cannot stack two Mopups pages sensibly — the second lands behind the
    /// first, which reads as a till that has frozen. So the caller is handed a signal, this closes,
    /// and the caller then opens the next dialog and (if it changed anything) reopens this.
    /// </summary>
    public static class CustomerDetailHelper
    {
        /// <summary>What the operator asked for on the way out.</summary>
        public enum Outcome
        {
            /// <summary>They closed it.</summary>
            Closed = 0,

            /// <summary>They pressed **Edit details**.</summary>
            Edit = 1,

            /// <summary>They pressed **Grant credit**.</summary>
            GrantCredit = 2,
        }

        public static async Task<Outcome> ShowAsync(
            Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto row,
            Plutus.Client.Core.PlutusApiClient.CustomerHistoryPage history,
            bool mayEdit,
            bool mayGrantCredit)
        {
            var result = Outcome.Closed;

            var body = new CustomerDetailAlert(
                row,
                history,
                // ⚠ Null when not permitted, which is what makes the button absent rather than
                // disabled — see the view.
                mayEdit ? () => result = Outcome.Edit : (Action)null,
                mayGrantCredit ? () => result = Outcome.GrantCredit : (Action)null);

            // ⚠ `interuptable: true` — clicking away closes it. This dialog decides nothing and holds
            // no half-finished state, so there is nothing to protect an operator from leaving.
            var popUp = new AlertDialogBase<bool>(body, true);

            // ⚠ EVERY EXIT COMPLETES THE SAME TASK. The two action buttons set `result` and then close
            // exactly as ✕ does, so there is one way out and no path that leaves the popup up.
            body.CloseRequested += (_, _) => popUp.PageClosedTaskCompletionSource.TrySetResult(true);

            await Services.UIHandeling.Modal.ShowAsync(async () =>
            {
                try
                {
                    await MopupService.Instance.PushAsync(popUp);
                    return await popUp.PageClosedTask;
                }
                finally
                {
                    try
                    {
                        await MopupService.Instance.PopAsync();
                    }
                    catch (Exception ex)
                    {
                        // ⚠ Swallowed only here: a failing pop must not replace the real exception.
                        Debug.WriteLine(ex);
                    }
                }
            });

            return result;
        }
    }
}
