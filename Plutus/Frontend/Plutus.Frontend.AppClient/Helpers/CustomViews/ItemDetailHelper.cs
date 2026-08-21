using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using CustomViews;
using Mopups.Services;
using Plutus.Frontend.AppClient.Views.CustomViews;

namespace Plutus.Frontend.AppClient.Helpers.CustomViews
{
    /// <summary>
    /// Show an item's barcodes and history, and hand back what the operator asked to do next — WP10.
    ///
    /// ⚠ ONE PUSH, ONE AWAIT, ONE POP **IN A `finally`** — the rule `InputAlertHelper.ShowAsync`'s
    /// header records the hard way. An un-popped popup is a 40%-black sheet over a till nobody can
    /// dismiss, mid-shift, with a full basket.
    ///
    /// ⚠⚠ THIS DIALOG DECIDES NOTHING AND WRITES NOTHING. It reports an intention — add, correct or
    /// remove a code — and the CALLER prompts and writes. That split exists because MAUI cannot stack
    /// two Mopups pages sensibly: the second lands behind the first and reads as a frozen till. Same
    /// reason `CustomerDetailHelper` works this way.
    /// </summary>
    public static class ItemDetailHelper
    {
        /// <summary>What the operator asked for on the way out. ⚠ `Closed` is a real answer.</summary>
        public sealed record Outcome(ItemDetailAlert.Kind Kind, string Code)
        {
            public static readonly Outcome Closed = new(ItemDetailAlert.Kind.Closed, null);

            /// <summary>They just left — the caller stops, and changes nothing.</summary>
            public bool IsClosed => Kind == ItemDetailAlert.Kind.Closed;
        }

        /// <param name="mayManage">Whether this operator may change barcodes — `pos.items.manage` or
        /// `portal.prices.manage`. ⚠ False hides the buttons rather than disabling them.</param>
        public static async Task<Outcome> ShowAsync(
            string itemIdOne,
            string itemName,
            IReadOnlyList<Plutus.Client.Core.PlutusApiClient.ItemBarcodeDto> barcodes,
            Plutus.Client.Core.PlutusApiClient.ItemHistoryPage history,
            bool mayManage)
        {
            var body = new ItemDetailAlert(itemIdOne, itemName, barcodes, history, mayManage);

            // ⚠ `interuptable: true` — clicking away closes it. Nothing here is half-finished, so there
            // is nothing to protect an operator from leaving.
            var popUp = new AlertDialogBase<bool>(body, true);

            // ⚠ EVERY EXIT COMPLETES THE SAME TASK — the ✕, Close, tapping away, and each of the three
            // action buttons. One way out, and no path that leaves the sheet up.
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

            // ⚠ READ AFTER THE AWAIT, never during. The view sets these on the UI thread as it closes.
            return body.Asked == ItemDetailAlert.Kind.Closed
                ? Outcome.Closed
                : new Outcome(body.Asked, body.AskedCode);
        }
    }
}
