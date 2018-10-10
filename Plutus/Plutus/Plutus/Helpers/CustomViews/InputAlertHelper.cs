using CustomViews;
using Plutus.Pages.CustomPages;
using Rg.Plugins.Popup.Services;
using System.Threading.Tasks;
using Plutus.Helpers.Extensions;
using System;
using Xamarin.Forms;
using System.Collections.Generic;

namespace Plutus.Helpers.CustomViews
{
    class InputAlertHelper
    {
        internal static async Task<decimal> LaunchInputAlertAsync(string title, string placeholder, string buttonText,
            string validText, decimal toPay = 0.0m, bool cash = false)
        {
            var inputAlert = new InputAlert(title, placeholder, buttonText, validText, cash, toPay);
            var popUp = new InputAlertDialogBase<string>(inputAlert);

            inputAlert.ConfirmButtonEHandler += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(((InputAlert) sender).InputResult))
                {
                    ((InputAlert) sender).IsValidationLVisable = false;

                    popUp.PageClosedTaskCompletionSource.SetResult(((InputAlert) sender).InputResult);
                }
                else
                {
                    ((InputAlert) sender).IsValidationLVisable = true;
                }
            };
            decimal? result = null;
            while (result == null)
            {
                await PopupNavigation.PushAsync(popUp);

                result = await (await popUp.PageClosedTask).ToDecimal("test");

                await PopupNavigation.PopAsync();
            }
            return (decimal) result;
        }

        internal static async Task<string> LaunchInputAlertAsync(string title, string placeholder, string buttonText,
            string validText, bool isPass = false)
        {
            var inputAlert = new InputAlert(title, placeholder, buttonText, validText, isPass);
            var popUp = new InputAlertDialogBase<string>(inputAlert);

            inputAlert.ConfirmButtonEHandler += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(((InputAlert) sender).InputResult))
                {
                    ((InputAlert) sender).IsValidationLVisable = false;

                    popUp.PageClosedTaskCompletionSource.SetResult(((InputAlert) sender).InputResult);
                }
                else
                {
                    ((InputAlert) sender).IsValidationLVisable = true;
                }
            };

            var result = "";

            while (result == "")
            {
                await PopupNavigation.PushAsync(popUp);

                result = await popUp.PageClosedTask;

                await PopupNavigation.PopAsync();
            }
            return result;
        }

        internal static async Task<List<Tuple<string, bool>>> LaunchInputAlertAsync(string titleText, Tuple<string, string, string, bool, bool>[] viewElements, string confirmButText)
        {
            var inputAlert = new InputAlert(titleText, viewElements, confirmButText);
            var popUp = new InputAlertDialogBase<List<Tuple<string, bool>>>(inputAlert);

            inputAlert.ConfirmButtonEHandler += (sender, e) =>
            {
                var page = (sender as InputAlert);
                var issue = false;
                for (var i = 0; i <= page.InputResults.Count - 1; i++)
                {
                    var inputResult = page.InputResults[i];
                    if (inputResult.Item2)
                    {
                        if (!string.IsNullOrEmpty(inputResult.Item1))
                        {
                            foreach (var viewElement in page.ViewElements)
                            {
                                if (viewElement.Item2 != null && (viewElement.Item2.ReturnCommandParameter as Tuple<bool, int>).Item2 == i)
                                {
                                    viewElement.Item3.IsVisible = false;
                                }
                            }
                        }
                        else
                        {
                            foreach (var viewElement in page.ViewElements)
                            {
                                if (viewElement.Item2 != null && (viewElement.Item2.ReturnCommandParameter as Tuple<bool, int>).Item2 == i)
                                {
                                    viewElement.Item3.IsVisible = true;
                                    issue = true;
                                }
                            }
                        }
                    }
                }
                if (!issue)
                {
                    popUp.PageClosedTaskCompletionSource.SetResult(((InputAlert)sender).InputResults);
                }
            };

            var result = new List<Tuple<string, bool>>();

            while(result.Count == 0/*Add check to ensure that all required fields are set*/)
            {
                await PopupNavigation.PushAsync(popUp);

                result = await popUp.PageClosedTask;

                await PopupNavigation.PopAsync();
            }

            return result;
        }
    }
}
