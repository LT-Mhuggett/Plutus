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
    /// **Ask the operator to pick one — the front door for `ChoiceAlert`.**
    ///
    /// ⚠⚠ THIS REPLACES `DisplayActionSheet` EVERYWHERE. Matt, 2026-08-21: *"the pop windows doesnt
    /// appear to follow the theme?"* — the native sheet is drawn by WinUI in the platform's own
    /// scheme and cannot be themed at all. See `ChoiceAlert` for why that is now a parity failure
    /// rather than an accepted limitation.
    ///
    /// ⚠ THE SIGNATURE MATCHES `Page.DisplayActionSheet` so a call site changes by one word:
    ///
    ///     await App.Current.MainPage.DisplayActionSheet(title, cancel, destruction, choices)
    ///     await ChoiceHelper.AskAsync(title, cancel, destruction, choices)
    ///
    /// ⚠ ONE PUSH, ONE AWAIT, ONE POP **IN A `finally`** — the rule `InputAlertHelper.ShowAsync`'s
    /// header records the hard way. An un-popped popup is a 40%-black sheet over a till nobody can
    /// dismiss: if anything between the push and the pop throws, the app goes dark, mid-shift.
    ///
    /// ⚠ THROUGH `Modal.ShowAsync`, like every other dialog here, so two cannot be raised over each
    /// other — and ⚠⚠ **NESTING IS SAFE, WHICH IS WHY THE MIGRATION WAS ONE WORD PER CALL SITE.**
    /// Almost every `DisplayActionSheet` was already inside a `Modal.ShowAsync(() => …)`, so this
    /// gating from within one looks like the 2026-08-13 checkout freeze and is not: `Modal` checks an
    /// `AsyncLocal` `Holding` flag and short-circuits the gate for a flow that already owns it. The
    /// outer wrapper at those 24 sites is now redundant, and harmless — ⚠ **leave it rather than
    /// tidying it in the same commit as a 24-site behavioural change**, or a regression has two
    /// candidate causes instead of one.
    /// </summary>
    public static class ChoiceHelper
    {
        /// <returns>The label picked, the cancel label if they backed out through it, or <c>null</c> if
        /// they used the ✕ or clicked away. ⚠ Every existing `DisplayActionSheet` caller already
        /// handles null, because the native sheet answers null the same way.</returns>
        public static async Task<string> AskAsync(
            string title, string cancel, string destruction, params string[] choices) =>
            await AskAsync(title, cancel, destruction, (IReadOnlyList<string>)choices);

        public static async Task<string> AskAsync(
            string title, string cancel, string destruction, IReadOnlyList<string> choices)
        {
            var body = new ChoiceAlert(title, cancel, destruction, choices);

            // ⚠ `interuptable: true` — clicking away backs out, and backing out is a real answer here.
            // This dialog decides nothing on its own; the caller acts on what comes back.
            var popUp = new AlertDialogBase<string>(body, true);

            body.CloseRequested += (_, _) => popUp.PageClosedTaskCompletionSource.TrySetResult(body.Picked);

            return await Services.UIHandeling.Modal.ShowAsync(async () =>
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
                        // ⚠ Swallowed on purpose, and only here: a pop that fails must not throw out of
                        // the `finally` and replace the real exception with this one.
                        Debug.WriteLine(ex);
                    }
                }
            });
        }
    }
}
