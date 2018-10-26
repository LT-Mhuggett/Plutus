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
        /// <summary>
        /// 
        /// </summary>
        /// <param name="titleText"></param>
        /// <param name="viewElements"></param>
        /// <param name="confirmButText"></param>
        /// <returns></returns>
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
                        if (viewElement.Item2 != null && (viewElement.Item2.ReturnCommandParameter as Tuple<bool, int>).Item2 == i)
                        {
                            if (!string.IsNullOrEmpty(inputResult.Item1))
                            {
                                data.Add(AddToDataResult(viewElement.Item3, inputResult.Item1, ref issue));
                                if (data[data.Count - 1] == null)
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
                                    if (data[data.Count - 1] == null)
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

                bool isFirstEntry = true;

                foreach (var item in inputAlert.ViewElements)
                    if (item.Item2 != null)
                        if (isFirstEntry)
                        {
                            item.Item2.Focus();
                            isFirstEntry = false;
                        }

                result = await popUp.PageClosedTask;

                await PopupNavigation.PopAsync();
            }

            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="titleText"></param>
        /// <param name="viewElements"></param>
        /// <param name="confirmButText"></param>
        /// <param name="cash"></param>
        /// <param name="toPay"></param>
        /// <returns></returns>
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

                bool isFirstEntry = true;

                foreach (var item in inputAlert.ViewElements)
                    if (item.Item2 != null)
                        if (isFirstEntry)
                        {
                            item.Item2.Focus();
                            isFirstEntry = false;
                        }
                
                result = await popUp.PageClosedTask;

                await PopupNavigation.PopAsync();
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
            catch(Exception ex)
            {
                result = null;
                Debug.WriteLine(ex);
            }

            return result;
        }
    }
}
