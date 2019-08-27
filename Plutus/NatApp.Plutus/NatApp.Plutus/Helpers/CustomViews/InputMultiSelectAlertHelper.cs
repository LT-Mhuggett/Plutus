using CustomViews;
using NatApp.Plutus.Helpers.Validators;
using NatApp.Plutus.Views.CustomViews;
using Rg.Plugins.Popup.Services;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace NatApp.Plutus.Helpers.CustomViews
{
    public class InputMultiSelectAlertHelper<T, T2>
    {
        public static async  Task<Tuple<IEnumerable<T>, IEnumerable<T2>>> LaunchInputMulitSelectAlertAsync(
            IEnumerable<Tuple<string, string, IEnumerable<IValidator>, bool, bool>> viewElementsBefore,
            Tuple<IEnumerable<T2>, string> itemsForList,
            IEnumerable<Tuple<string, string, IEnumerable<IValidator>, bool, bool>> viewElementsAfter,
            string confirmButText, bool interuptable, string title = null)
        {
            var inputMSAlert = InputMultiSelectAlert<T2>.InputMultiSelectAlertFactory(viewElementsBefore, itemsForList, viewElementsAfter, confirmButText, title);
            inputMSAlert.Initalize();

            var popUp = new AlertDialogBase<Tuple<IEnumerable<T>, IEnumerable<T2>>>(inputMSAlert, interuptable);

            inputMSAlert.ConfirmButtonEHandler += (sender, e) =>
              {
                  var page = (sender as InputMultiSelectAlert<T2>);
                  var data1 = new List<T>();
                  foreach (var inputResult in page.InputResults.Values)
                      if(inputResult is T confInputResult)
                      data1.Add(confInputResult);

                  var data2 = page.SelectedItems();
                    popUp.PageClosedTaskCompletionSource.SetResult(Tuple.Create<IEnumerable<T>, IEnumerable<T2>>(data1, data2));
              };

            var result = default(Tuple<IEnumerable<T>, IEnumerable<T2>>);

            while(result == default(Tuple<IEnumerable<T>, IEnumerable<T2>>))
            {
                await PopupNavigation.Instance.PushAsync(popUp);

                bool isFistEntry = true;

                foreach(var item in inputMSAlert.ViewElements)
                {
                    if (item.Item2 != null)
                        if (isFistEntry)
                        {
                            item.Item2.Focus();
                            isFistEntry = false;
                        }
                }
                result = await popUp.PageClosedTask;
                await PopupNavigation.Instance.PopAsync();
            }
            return result;
        }
    }
}
