using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Plutus.Helpers.Extensions;
using CustomViews;
using Plutus.Models;
using Plutus.Pages.CustomPages;
using Rg.Plugins.Popup.Services;

namespace Plutus.Helpers.CustomViews
{
    internal static class DataPickerInputAlertHelper
    {
        internal static async Task<Tuple<CategoryModel, DateTime?, DateTime?>> LaunchDataPickerInputAlertAsync(
            string title, string
                buttonText, string validText, List<CategoryModel> list)
        {
            var dPInputAlert = new DataPickerInputAlert(title, buttonText, validText, list);
            var popUp = new InputAlertDialogBase<Tuple<CategoryModel, DateTime?, DateTime?>>(dPInputAlert);

            dPInputAlert.ConfirmButtonEHandler += (sender, e) =>
            {
                var obj = (DataPickerInputAlert) sender;
                if (obj.obj != null)
                {
                    obj.IsValidationLVisable = false;

                    var tuple =
                        Tuple.Create<CategoryModel, DateTime?, DateTime?>(
                            obj.obj.ToModel<CategoryModel>(), obj.StartDate, obj.EndDate);
                    popUp.PageClosedTaskCompletionSource.SetResult(tuple);
                }
                else
                {
                    obj.IsValidationLVisable = true;
                }
            };

            dPInputAlert.CloseButtonEHandler += (sender, e) =>
            {
                popUp.PageClosedTaskCompletionSource.SetResult(
                    Tuple.Create<CategoryModel, DateTime?, DateTime?>(null, null, null));
            };

            var result = Tuple.Create<CategoryModel, DateTime?, DateTime?>(null, DateTime.MinValue, DateTime.MinValue);
            while (result.Item1 == null && result.Item2 == DateTime.MinValue && result.Item3 == DateTime.MinValue)
            {
                await PopupNavigation.PushAsync(popUp);

                result = await popUp.PageClosedTask;

                await PopupNavigation.PopAsync();
            }
            return result;
        }

        internal static async Task<Tuple<ItemModel, DateTime?, DateTime?>> LaunchDataPickerInputAlertAsync(
            string title, string
                buttonText, string validText, List<ItemModel> list)
        {
            var dPInputAlert = new DataPickerInputAlert(title, buttonText, validText, list);
            var popUp = new InputAlertDialogBase<Tuple<ItemModel, DateTime?, DateTime?>>(dPInputAlert);

            dPInputAlert.ConfirmButtonEHandler += (sender, e) =>
            {
                var obj = (DataPickerInputAlert) sender;
                if (obj.obj != null)
                {
                    obj.IsValidationLVisable = false;

                    var tuple =
                        Tuple.Create<ItemModel, DateTime?, DateTime?>(obj.obj.ToModel<ItemModel>(),
                            obj.StartDate, obj.EndDate);
                    popUp.PageClosedTaskCompletionSource.SetResult(tuple);
                }
                else
                {
                    obj.IsValidationLVisable = true;
                }
            };

            dPInputAlert.CloseButtonEHandler += (sender, e) =>
            {
                popUp.PageClosedTaskCompletionSource.SetResult(
                    Tuple.Create<ItemModel, DateTime?, DateTime?>(null, null, null));
            };

            var result = Tuple.Create<ItemModel, DateTime?, DateTime?>(null, DateTime.MinValue, DateTime.MinValue);
            while (result.Item1 == null && result.Item2 == DateTime.MinValue && result.Item3 == DateTime.MinValue)
            {
                await PopupNavigation.PushAsync(popUp);

                result = await popUp.PageClosedTask;

                await PopupNavigation.PopAsync();
            }
            return result;
        }
    }
}