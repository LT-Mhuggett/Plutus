using CustomViews;
using Plutus.Pages.CustomPages;
using Rg.Plugins.Popup.Services;
using System.Threading.Tasks;
using Plutus.Helpers.Extensions;
using System;
using Xamarin.Forms;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace Plutus.Helpers.CustomViews
{
    class InputAlertHelper
    {
        /*
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
        }*/
        /*
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
        */
        internal static async Task<List<object>> LaunchInputAlertAsync(string titleText, Tuple<string, string, Type, string, bool, bool>[] viewElements, string confirmButText)
        {
            var inputAlert = new InputAlert(titleText, viewElements, confirmButText);
            var popUp = new InputAlertDialogBase<List<object>>(inputAlert);

            inputAlert.ConfirmButtonEHandler += (sender, e) =>
            {
                var page = (sender as InputAlert);
                var data = new List<object>();
                var issue = false;
                for (var i = 0; i <= page.InputResults.Count - 1; i++)
                {
                    var inputResult = page.InputResults[i];
                    foreach (var viewElement in page.ViewElements)
                    {
                        data.Add(inputResult.Item1);
                        if (viewElement.Item2 != null && (viewElement.Item2.ReturnCommandParameter as Tuple<bool, int>).Item2 == i)
                        {
                            if (!string.IsNullOrEmpty(inputResult.Item1))
                            {
                                data.Add(AddToDataResult(viewElement.Item3, inputResult.Item1, ref issue));
                                if (data[data.Count - 1] != null)
                                    viewElement.Item4.IsVisible = true;
                                else
                                    viewElement.Item4.IsVisible = false;
                            }
                            else
                            {
                                if (inputResult.Item2)
                                {
                                    viewElement.Item4.IsVisible = true;
                                    issue = true;
                                    data.Add(null);
                                }
                                else
                                {
                                    data.Add(AddToDataResult(viewElement.Item3, inputResult.Item1, ref issue));
                                    if (data[data.Count - 1] != null)
                                        viewElement.Item4.IsVisible = true;
                                    else
                                        viewElement.Item4.IsVisible = false;
                                }

                            }
                        }
                    }
                }
                if (!issue)
                {
                    popUp.PageClosedTaskCompletionSource.SetResult(data);
                }
            };

            var result = new List<object>();

            while(result.Count == 0/*Add check to ensure that all required fields are set*/)
            {
                await PopupNavigation.PushAsync(popUp);

                result = await popUp.PageClosedTask;

                await PopupNavigation.PopAsync();
            }

            return result;
        }

        internal static async Task<List<object>> LaunchInputAlertAsync(string titleText, Tuple<string, string, Type, string, bool, bool>[] viewElements, string confirmButText, bool cash, decimal toPay)
        {
            var inputAlert = new InputAlert(titleText, viewElements, confirmButText, cash, toPay);
            var popUp = new InputAlertDialogBase<List<object>>(inputAlert);

            inputAlert.ConfirmButtonEHandler += (sender, e) =>
            {
                var page = (sender as InputAlert);
                var data = new List<object>();
                var issue = false;
                for (var i = 0; i <= page.InputResults.Count - 1; i++)
                {
                    var inputResult = page.InputResults[i];
                    foreach (var viewElement in page.ViewElements)
                    {
                        data.Add(inputResult.Item1);
                        if (viewElement.Item2 != null && (viewElement.Item2.ReturnCommandParameter as Tuple<bool, int>).Item2 == i)
                        {
                            if (!string.IsNullOrEmpty(inputResult.Item1))
                            {
                                data.Add(AddToDataResult(viewElement.Item3, inputResult.Item1, ref issue));
                                if (data[data.Count - 1] != null)
                                    viewElement.Item4.IsVisible = true;
                                else
                                    viewElement.Item4.IsVisible = false;
                            }
                            else
                            {
                                if (inputResult.Item2)
                                {
                                    viewElement.Item4.IsVisible = true;
                                    issue = true;
                                    data.Add(null);
                                }
                                else
                                {
                                    data.Add(AddToDataResult(viewElement.Item3, inputResult.Item1, ref issue));
                                    if (data[data.Count - 1] != null)
                                        viewElement.Item4.IsVisible = true;
                                    else
                                        viewElement.Item4.IsVisible = false;
                                }

                            }
                        }
                    }
                }
                if (!issue)
                {
                    popUp.PageClosedTaskCompletionSource.SetResult(data);
                }
            };

            var result = new List<object>();

            while (result.Count == 0/*Add check to ensure that all required fields are set*/)
            {
                await PopupNavigation.PushAsync(popUp);

                result = await popUp.PageClosedTask;

                await PopupNavigation.PopAsync();
            }

            return result;
        }

        private static object AddToDataResult(Type type, string value, ref bool issue)
        {
            object result;
            try
            {
                result = TypeDescriptor.GetConverter(type).ConvertFromString(value);
            }
            catch(Exception ex)
            {
                result = null;
                Debug.WriteLine(ex);
            }

            return result;
        }
    }
}
