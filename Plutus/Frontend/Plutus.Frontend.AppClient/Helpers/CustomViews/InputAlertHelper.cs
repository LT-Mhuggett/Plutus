using CustomViews;
using CustomViews.Structs;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Pages.CustomViews;
using Mopups.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Plutus.Frontend.AppClient.Helpers.CustomViews
{
    public class InputAlertHelper
    {
        /// <summary>
        /// A VISIBLE way out, on every dialog, by default.
        ///
        /// ⚠⚠ WHY THIS EXISTS. Matt, 2026-08-18, on the price-adjust box: *"the box it pops has no X
        /// to close the box. I know you can click outside of the box to close it but its not
        /// intuative."* He is right, and the interesting part is that backing out ALREADY WORKED —
        /// `AlertDialogBase.OnBackButtonPressed` always cancels, and `OnBackgroundClicked` cancels
        /// whenever the dialog is `interuptable`. What was missing was any way to KNOW that.
        ///
        /// ⚠ So this is a discoverability fix, not a capability one: it cannot change what a dialog
        /// permits, only what it advertises. That is why it is safe to apply to every caller at once —
        /// 24 call sites, of which most passed no `cancelText` and so drew no button.
        ///
        /// ⚠ Callers already treat a null result as "the operator backed out" (`if (data == null …)
        /// return;`), which is exactly what Cancel produces. Nothing downstream needs to change.
        ///
        /// ⚠ TO SUPPRESS IT DELIBERATELY, pass <see cref="string.Empty"/> — null now means "give me
        /// the default". There is currently no caller that should: a dialog an operator cannot leave
        /// is a till a shop cannot use, which is the fault `OnBackButtonPressed`'s header records.
        /// </summary>
        private const string DefaultCancelKey = "Cancel";

        /// <summary>
        ///
        /// </summary>
        /// <param name="viewElements"></param>
        /// <param name="confirmButText"></param>
        /// <param name="interuptable"></param>
        /// <param name="titleText"></param>
        /// <param name="cancelText">⚠ Null = the standard "Cancel". Pass "" to suppress — see
        /// <see cref="DefaultCancelKey"/>.</param>
        /// <returns></returns>
        public static async Task<Dictionary<uint, string>> LaunchInputAlertAsync(IEnumerable<ViewElementData> viewElements, string confirmButText, bool interuptable, string titleText = null, string cancelText = null)
        {
            var inputAlert = new InputAlert(viewElements, confirmButText, titleText, cancelText ?? DefaultCancelKey.Translate());
            var popUp = new AlertDialogBase<Dictionary<uint, string>>(inputAlert, interuptable);

            inputAlert.ConfirmButtonEHandler += (sender, e) =>
            {
                var page = (sender as InputAlert);
                popUp.PageClosedTaskCompletionSource.TrySetResult(page.InputResults);
            };

            return await ShowAsync(popUp, inputAlert);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="viewElements"></param>
        /// <param name="confirmButText"></param>
        /// <param name="cash"></param>
        /// <param name="toPay"></param>
        /// <param name="titleText"></param>
        /// <returns></returns>
        public static async Task<Dictionary<uint, string>> LaunchInputAlertAsync(IEnumerable<ViewElementData> viewElements, string confirmButText, bool interuptable, bool cash, decimal toPay, string titleText = null, string cancelText = null)
        {
            // ⚠ Same default as the overload above — and this is the CASH PAYMENT dialog, the one
            // `AlertDialogBase`'s header names as having had no exit at all.
            var inputAlert = new InputAlert(viewElements, confirmButText, cash, toPay, titleText, cancelText ?? DefaultCancelKey.Translate());
            var popUp = new AlertDialogBase<Dictionary<uint, string>>(inputAlert, interuptable);

            inputAlert.ConfirmButtonEHandler += (sender, e) =>
            {
                var page = (sender as InputAlert);
                popUp.PageClosedTaskCompletionSource.TrySetResult(page.InputResults);
            };

            return await ShowAsync(popUp, inputAlert);
        }

        /// <summary>
        /// Show the popup, wait for it, and — whatever happens — TAKE IT DOWN AGAIN.
        ///
        /// ⚠ THE POP WAS NOT GUARANTEED, and an un-popped popup is a 40%-black sheet over a till
        /// nobody can dismiss. If anything between the push and the pop threw — and `Entry.Focus()`
        /// on a not-yet-realised handler is exactly that sort of thing — the exception left the
        /// popup on the stack for good. The whole app went dark, mid-sale, with no dialog on it.
        ///
        /// ⚠ AND THE LOOP COULD NEVER TERMINATE. This was wrapped in
        /// `while (result.Count == 0) { push; await PageClosedTask; pop; }`, over a
        /// `TaskCompletionSource` created ONCE in the popup's constructor. A second pass awaits an
        /// already-completed task, so any result the loop rejected span the UI thread at full speed
        /// — push, pop, push — for ever. There is no input that recovers from that.
        ///
        /// One push. One await. One pop, in a `finally`.
        ///
        /// ⚠ Returns an EMPTY dictionary when the operator backed out (Cancel, Escape, or clicking
        /// away from an interruptible dialog). Callers must treat empty as "they changed their
        /// mind" and unwind — never as "they entered nothing", which is how a cancelled payment
        /// turns into a re-prompt the operator cannot escape.
        /// </summary>
        private static async Task<Dictionary<uint, string>> ShowAsync(
            AlertDialogBase<Dictionary<uint, string>> popUp, InputAlert inputAlert)
        {
            return await Services.UIHandeling.Modal.ShowAsync(async () =>
            {
            try
            {
                await MopupService.Instance.PushAsync(popUp);

                try
                {
                    // ⚠ `ViewElement` is a STRUCT, so `FirstOrDefault(...)?.Entry` does not compile
                    // and `FirstOrDefault()` on no match hands back a zeroed struct rather than null.
                    var firstEntry = inputAlert.ViewElements
                        .Where(v => v.Entry != null).Select(v => v.Entry).FirstOrDefault();
                    firstEntry?.Focus();
                }
                catch (Exception ex)
                {
                    // ⚠ Focus is a convenience. It must never be the reason a dialog is unusable.
                    Debug.WriteLine(ex);
                }

                return await popUp.PageClosedTask ?? new Dictionary<uint, string>();
            }
            finally
            {
                try
                {
                    await MopupService.Instance.PopAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
            });
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="type"></param>
        /// <param name="value"></param>
        /// <param name="issue"></param>
        /// <returns></returns>
        private static object AddToDataResult(Type type, string value, ref bool issue)
        {
            object result;
            try
            {
                result = TypeDescriptor.GetConverter(type).ConvertFromString(value);
            }
            catch (Exception ex)
            {
                result = null;
                Debug.WriteLine(ex);
            }

            return result;
        }
    }
}
