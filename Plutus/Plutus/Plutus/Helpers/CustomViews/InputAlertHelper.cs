using CustomViews;
using Plutus.Pages.CustomPages;
using Rg.Plugins.Popup.Services;
using System.Threading.Tasks;

namespace Plutus.Helpers.CustomViews
{
    class InputAlertHelper
    {
        internal static async Task<decimal> LaunchInputAlertAsync(string title, string placeholder, string buttonText, string validText, decimal toPay = 0.0m, bool cash = false)
        {
            var inputAlert = new InputAlert(title, placeholder, buttonText, validText, cash, toPay);
            var popUp = new InputAlertDialogBase<string>(inputAlert);

            inputAlert.ConfirmButtonEHandler += (sender, e) =>
              {
                  if (!string.IsNullOrEmpty(((InputAlert)sender).InputResult))
                  {
                      ((InputAlert)sender).IsValidationLVisable = false;

                      popUp.PageClosedTaskCompletionSource.SetResult(((InputAlert)sender).InputResult);
                  }
                  else
                  {
                      ((InputAlert)sender).IsValidationLVisable = true;
                  }
              };
            decimal? result=null;
            while (result == null) {
                await PopupNavigation.PushAsync(popUp);

                result = await Conversions.ToDecimal(await popUp.PageClosedTask, "test");

                await PopupNavigation.PopAsync();
            }
            return (decimal)result;
        } 
    }
}
