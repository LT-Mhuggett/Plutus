using CustomViews;
using Plutus.Frontend.AppClient.Views.CustomViews;
using Mopups.Services;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Plutus.Frontend.AppClient.Helpers.CustomViews
{
    public class SliderAlertHelper
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="viewElements"></param>
        /// <param name="confrimBut"></param>
        /// <param name="interuptable"></param>
        /// <param name="titleText"></param>
        /// <returns></returns>
        public static async Task<List<string>> LaunchSliderAlertAsync(Queue<Tuple<string, List<string>, string>> viewElements, string confrimBut, bool interuptable, string titleText = null)
        {
            var sliderAlert = new SliderAlert(viewElements, confrimBut, titleText);
            var popUp = new AlertDialogBase<List<string>>(sliderAlert, interuptable);

            sliderAlert.ConfirmButtonEHandler += (sender, e) =>
            {
                var page = (sender as SliderAlert);
                var data = new List<string>();
                foreach (var sliderResult in page.SliderResults.Values)
                    data.Add(sliderResult);
                popUp.PageClosedTaskCompletionSource.TrySetResult(data);
            };

            // ⚠ ONE PUSH, ONE AWAIT, ONE POP IN A `finally` — see `InputAlertHelper.ShowAsync`,
            // which carries the full explanation. This read `while (result.Count == 0) { push;
            // await; pop; }` over a TaskCompletionSource created ONCE, so any empty result span the
            // UI thread for ever, and an un-popped popup leaves a 40%-black sheet over the till.
            // ⚠ `TrySetResult`, and a null-safe result: the dialog can now also be cancelled with
            // Escape, which completes the task with `default` — i.e. a NULL list here.
            try
            {
                await MopupService.Instance.PushAsync(popUp);
                return await popUp.PageClosedTask ?? new List<string>();
            }
            finally
            {
                try { await MopupService.Instance.PopAsync(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            }
        }
    }
}
