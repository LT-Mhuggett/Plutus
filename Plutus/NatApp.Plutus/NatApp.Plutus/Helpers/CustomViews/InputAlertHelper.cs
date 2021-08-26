using CustomViews;
using CustomViews.Structs;
using NatApp.Plutus.Pages.CustomViews;
using Rg.Plugins.Popup.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace NatApp.Plutus.Helpers.CustomViews
{
    public class InputAlertHelper
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="viewElements"></param>
        /// <param name="confirmButText"></param>
        /// <param name="interuptable"></param>
        /// <param name="titleText"></param>
        /// <returns></returns>
        public static async Task<Dictionary<uint, string>> LaunchInputAlertAsync(IEnumerable<ViewElementData> viewElements, string confirmButText, bool interuptable, string titleText = null, string cancelText = null)
        {
            var inputAlert = new InputAlert(viewElements, confirmButText, titleText, cancelText);
            var popUp = new AlertDialogBase<Dictionary<uint, string>>(inputAlert, interuptable);

            inputAlert.ConfirmButtonEHandler += (sender, e) =>
            {
                var page = (sender as InputAlert);
                popUp.PageClosedTaskCompletionSource.SetResult(page.InputResults);
            };

            var result = new Dictionary<uint, string>();

            while (result.Count == 0/*Add check to ensure that all required fields are set*/)
            {
                await PopupNavigation.Instance.PushAsync(popUp);

                bool isFirstEntry = true;

                foreach (var item in inputAlert.ViewElements)
                    if (item.Entry != null)
                        if (isFirstEntry)
                        {
                            item.Entry.Focus();
                            isFirstEntry = false;
                        }

                result = await popUp.PageClosedTask;

                await PopupNavigation.Instance.PopAsync();
            }

            return result;
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
            var inputAlert = new InputAlert(viewElements, confirmButText, cash, toPay, titleText, cancelText);
            var popUp = new AlertDialogBase<Dictionary<uint, string>>(inputAlert, interuptable);

            inputAlert.ConfirmButtonEHandler += (sender, e) =>
            {
                var page = (sender as InputAlert);
                popUp.PageClosedTaskCompletionSource.SetResult(page.InputResults);
            };

            var result = new Dictionary<uint, string>();

            while (result.Count == 0/*Add check to ensure that all required fields are set*/)
            {
                await PopupNavigation.Instance.PushAsync(popUp);

                bool isFirstEntry = true;

                foreach (var item in inputAlert.ViewElements)
                    if (item.Entry != null)
                        if (isFirstEntry)
                        {
                            item.Entry.Focus();
                            isFirstEntry = false;
                        }

                result = await popUp.PageClosedTask;

                await PopupNavigation.Instance.PopAsync();
            }

            return result;
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
