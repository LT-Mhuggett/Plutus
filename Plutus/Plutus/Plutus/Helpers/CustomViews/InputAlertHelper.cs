using System;
using System.Collections.Generic;
using System.Text;
using CustomViews;
using Rg.Plugins.Popup;
using System.Threading.Tasks;
using Rg.Plugins.Popup.Services;
using Plutus.Pages.CustomPages;

namespace Plutus.Helpers.CustomViews
{
    class InputAlertHelper
    {
        internal static async Task<decimal> LaunchInputAlertAsync(string Title, string Placeholder, string ButtonText, string ValidText)
        {
            var InputAlert = new InputAlert(Title, Placeholder, ButtonText, ValidText);
            var PopUp = new InputAlertDialogBase<string>(InputAlert);

            InputAlert.ConfirmButtonEHandler += (sender, e) =>
              {
                  if (!string.IsNullOrEmpty(((InputAlert)sender).InputResult))
                  {
                      ((InputAlert)sender).IsValidationLVisable = false;

                      PopUp.PageClosedTaskCompletionSource.SetResult(((InputAlert)sender).InputResult);
                  }
                  else
                  {
                      ((InputAlert)sender).IsValidationLVisable = true;
                  }
              };
            decimal? result=null;
            while (result == null) {
                await PopupNavigation.PushAsync(PopUp);

                result = await Conversions.ToDecimal(await PopUp.PageClosedTask, "test");

                await PopupNavigation.PopAsync();
            }
            return (decimal)result;
        } 
    }
}
