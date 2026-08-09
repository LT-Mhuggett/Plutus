using CustomViews;
using CustomViews.Structs;
using Plutus.Frontend.AppClient.Views.CustomViews;
using Mopups.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Plutus.Frontend.AppClient.Helpers.CustomViews
{
    public class InputMultiSelectAlertHelper<T, T2>
    {
        public static async Task<Tuple<IEnumerable<T>, IEnumerable<T2>>> LaunchInputMulitSelectAlertAsync(
            IEnumerable<ViewElementData> viewElementsBefore,
            Tuple<IEnumerable<T2>, string> itemsForList,
            IEnumerable<ViewElementData> viewElementsAfter,
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
                      if (inputResult is T confInputResult)
                          data1.Add(confInputResult);

                  var data2 = page.SelectedItems();
                  popUp.PageClosedTaskCompletionSource.TrySetResult(Tuple.Create<IEnumerable<T>, IEnumerable<T2>>(data1, data2));
              };

            // ⚠ ONE PUSH, ONE AWAIT, ONE POP IN A `finally` — see `InputAlertHelper.ShowAsync` for
            // the full explanation. The `while (result == default)` this replaces was the worst of
            // the three: cancelling now completes the task with `default`, which was precisely the
            // loop's continue condition, so backing out span the UI thread for ever.
            // ⚠ Returns NULL when the operator backed out. Callers must null-check.
            try
            {
                await MopupService.Instance.PushAsync(popUp);

                try
                {
                    var firstEntry = inputMSAlert.ViewElements
                        .Where(v => v.Entry != null).Select(v => v.Entry).FirstOrDefault();
                    firstEntry?.Focus();
                }
                catch (Exception ex)
                {
                    // ⚠ Focus is a convenience. It must never be the reason a dialog is unusable.
                    System.Diagnostics.Debug.WriteLine(ex);
                }

                return await popUp.PageClosedTask;
            }
            finally
            {
                try { await MopupService.Instance.PopAsync(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            }
        }
    }
}
