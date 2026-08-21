using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CustomViews;
using Mopups.Services;
using Plutus.Contracts.Client;
using Plutus.Frontend.AppClient.Views.CustomViews;

namespace Plutus.Frontend.AppClient.Helpers.CustomViews
{
    /// <summary>
    /// Show one sale — the drill-down's front door (§5c item 5).
    ///
    /// ⚠ ONE PUSH, ONE AWAIT, ONE POP **IN A `finally`** — the rule `InputAlertHelper.ShowAsync`'s
    /// header records the hard way. An un-popped popup is a 40%-black sheet over a till nobody can
    /// dismiss: if anything between the push and the pop throws, the app goes dark, mid-shift, with
    /// no dialog on it.
    ///
    /// ⚠ THROUGH `Modal.ShowAsync`, like every other dialog here, so two dialogs cannot be raised
    /// over each other — the fault behind finding U.
    /// </summary>
    public static class SaleDetailHelper
    {
        /// <param name="tillLabel">Which till took it — passed in because `GET /api/v1/sales/{id}`
        /// does not answer a till, while the list the operator tapped does. ⚠ Better a fragment of an
        /// id from the row than a blank column on a cross-till report.</param>
        public static async Task ShowAsync(SaleDto sale, string tillLabel)
        {
            // ⚠⚠ THE REPRINT IS WIRED HERE, not in the view. `SaleDetailAlert` knows what a button
            // looks like and nothing about printers; `ReceiptReprint` knows about printers and nothing
            // about dialogs. Handing the view a `Func<Task>` keeps it that way — and it is what lets
            // the alert stay a dumb renderer that a test can build without a print stack behind it.
            //
            // ⚠ `ReprintAsync` ALREADY HANDLES EVERY CASE this dialog could hit: the sale is on this
            // till, or it is another till's and comes from the platform; the printer is set, unset, or
            // refuses — and each of those ends in either paper or an offer to show the receipt on
            // screen. None of that is re-derived here, which is the point.
            //
            // ⚠ It marks the paper `(COPY)` (`IsReprint = true`) and does NOT kick the drawer. Both
            // are money rules, both live in `ReceiptReprint`, and neither is this file's business.
            var body = new SaleDetailAlert(
                sale, tillLabel, () => Services.Printing.ReceiptReprint.ReprintAsync(sale.Id));

            // ⚠ `interuptable: true` — clicking away closes it. This dialog decides nothing and holds
            // no money, so there is no half-finished state to protect an operator from leaving.
            var popUp = new AlertDialogBase<bool>(body, true);

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
                        // ⚠ Swallowed on purpose, and only here: a pop that fails must not throw out
                        // of the `finally` and replace the real exception with this one.
                        Debug.WriteLine(ex);
                    }
                }
            });
        }
    }
}
