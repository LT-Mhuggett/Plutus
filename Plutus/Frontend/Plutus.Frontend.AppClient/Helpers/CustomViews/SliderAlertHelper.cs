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
                popUp.PageClosedTaskCompletionSource.SetResult(data);
            };

            var result = new List<string>();
            while(result.Count == 0)
            {
                await MopupService.Instance.PushAsync(popUp);

                result = await popUp.PageClosedTask;

                await MopupService.Instance.PopAsync();
            }

            return result;
        }
    }
}
