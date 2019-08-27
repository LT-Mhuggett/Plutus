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
        internal static async Task<Tuple<List<string>, List<dynamic>>> LaunchInputWithMultiSelectionAsync(
            string title, List<string> placeholders, string buttonText, List<string> validationList, List<dynamic> selections,
            List<string> bindingNames, string staticInput = null)
        {
            var dPInputWithListMultiSelection = new InputWithListMultiSelection(title, placeholders, 
                buttonText, validationList,
                selections, bindingNames, staticInput);
            var popUp = new AlertDialogBase<Tuple<List<string>, List<dynamic>>>(dPInputWithListMultiSelection);
            dPInputWithListMultiSelection.ConfirmButtonEHandler += (sender, e) =>
            {
                var items = new List<dynamic>();
                foreach (var item in dPInputWithListMultiSelection.ListOfItems)
                {
                    if (item.IsSelected)
                    {
                        items.Add(item.Data);
                    }
                }

                var inputs = new List<string>();
                foreach (var entry in dPInputWithListMultiSelection.Entries)
                {
                    decimal temp;

                    if (entry.Text != null && Decimal.TryParse(entry.Text, out temp))
                    {
                        inputs.Add(entry.Text);
                    }
                }
                if (items.Count != 0 && inputs.Count != 0)
                {
                    var tuple = Tuple.Create(inputs, items);
                    popUp.PageClosedTaskCompletionSource.SetResult(tuple);
                }
            };

            var result = Tuple.Create<List<string>, List<dynamic>>(null, null);
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
