using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CustomViews;
using Plutus.Pages.CustomPages;
using Rg.Plugins.Popup.Services;

namespace Plutus.Helpers.CustomViews
{
    class InputWithMultiSelection
    {
        internal static async Task<Tuple<List<string>, List<List<T>>>> LaunchInputWithMultiSelectionAsync<T>(
            string title, List<string> placeholders, string buttonText, List<string> validationList, List<List<T>> selections,
            List<string> bindingNames)
        {
            var dPInputWithListMultiSelection = new InputWithListMultiSelection<T>(title, placeholders, 
                buttonText, validationList,
                selections, bindingNames);
            var popUp = new InputAlertDialogBase<Tuple<List<string>, List<List<T>>>>(dPInputWithListMultiSelection);
            dPInputWithListMultiSelection.ConfirmButtonEHandler += (sender, e) =>
            {
                var data = new List<List<T>>();
                foreach (var list in dPInputWithListMultiSelection.ListOfListItems)
                {
                    var items = new List<T>();
                    foreach (var item in list)
                    {
                        if (item.IsSelected)
                        {
                            items.Add(item.Data);
                        }
                    }

                    data.Add(items);
                }

                var inputs = new List<string>();
                foreach (var entry in dPInputWithListMultiSelection.Entries)
                {
                    if (entry.Text != null)
                    {
                        inputs.Add(entry.Text);
                    }
                }

                var tuple = Tuple.Create(inputs, data);
                popUp.PageClosedTaskCompletionSource.SetResult(tuple);
            };

            var result = Tuple.Create<List<string>, List<List<T>>>(null, null);
            while (result.Item1 == null && result.Item2 == null)
            {
                await PopupNavigation.PushAsync(popUp);

                result = await popUp.PageClosedTask;

                await PopupNavigation.PopAsync();
            }

            return result;
        }
    }
}
