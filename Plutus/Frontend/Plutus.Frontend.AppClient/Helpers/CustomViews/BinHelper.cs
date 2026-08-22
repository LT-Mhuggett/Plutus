using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using CustomViews;
using Mopups.Services;
using Plutus.Contracts.Client;
using Plutus.Frontend.AppClient.Views.CustomViews;

namespace Plutus.Frontend.AppClient.Helpers.CustomViews
{
    /// <summary>
    /// Show the Bin — WP10 #4's front door.
    ///
    /// ⚠ ONE PUSH, ONE AWAIT, ONE POP **IN A `finally`** — the rule `InputAlertHelper.ShowAsync`'s
    /// header records the hard way. An un-popped popup is a 40%-black sheet over a till nobody can
    /// dismiss: if anything between the push and the pop throws, the app goes dark, mid-shift.
    ///
    /// ⚠ THROUGH `Modal.ShowAsync`, like every other dialog here, so two cannot be raised over each
    /// other.
    /// </summary>
    public static class BinHelper
    {
        /// <summary>What the operator asked for. ⚠ `IsClosed` is a real answer, not an absence (D4).</summary>
        public readonly record struct Outcome(BinAlert.Kind Kind, string IdOne)
        {
            public bool IsClosed => Kind == BinAlert.Kind.Closed;
        }

        /// <param name="items">⚠ NULL means the Bin could not be READ, which the dialog renders
        /// differently from an empty one — see `BinAlert`.</param>
        public static async Task<Outcome> ShowAsync(IReadOnlyList<BinnedItemDto> items, bool mayRestore)
        {
            var body = new BinAlert(items, mayRestore);

            // ⚠ `interuptable: true` — clicking away closes it. This dialog decides nothing on its
            // own; the caller acts on what comes back.
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

            return new Outcome(body.Asked, body.AskedIdOne);
        }
    }
}
