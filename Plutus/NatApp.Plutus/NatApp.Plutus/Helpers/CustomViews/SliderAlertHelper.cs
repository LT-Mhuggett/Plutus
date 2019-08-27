using CustomViews;
using NatApp.Plutus.Views.CustomViews;
using Rg.Plugins.Popup.Services;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace NatApp.Plutus.Helpers.CustomViews
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
                await PopupNavigation.Instance.PushAsync(popUp);

                result = await popUp.PageClosedTask;

                await PopupNavigation.Instance.PopAsync();
            }

            return result;
        }
    }
}
