using Plutus.Frontend.ClientUI.Core.CustomViews;
using Plutus.Frontend.ClientUI.Core.Validators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UIKit;

namespace Plutus.Frontend.ClientUI.Platforms.iOS.Core.CustomViews
{
    public class InputAlertWithValidation<T> where T : class
    {
        UIAlertController AlertController { get; set; }
        IList<IValidator> Validators { get; set; }
        UIAlertAction ConfirmButton { get; set; }

        public InputAlertWithValidation(InputData<T> inputData, string title, string message, string confirmButtonText, string cancelButtonText = null)
        {
            AlertController = UIAlertController.Create(title, message, UIAlertControllerStyle.Alert);
            ConfirmButton = UIAlertAction.Create(confirmButtonText, UIAlertActionStyle.Default, null);
            ConfirmButton.Enabled = false;
            AlertController.AddAction(ConfirmButton);

            if(cancelButtonText != null)
                AlertController.AddAction(UIAlertAction.Create(cancelButtonText, UIAlertActionStyle.Cancel, null));

            AlertController.AddTextField(textField =>
            {
                textField.Text = inputData.Value.ToString();
                textField.Placeholder = inputData.Placeholder.ToString();
                textField.SecureTextEntry = inputData.SecureInput;
                textField.EditingDidEndOnExit += TextField_EditingDidEndOnExit;
            });
            Validators = inputData.Validators;
        }

        private void TextField_EditingDidEndOnExit(object sender, EventArgs e)
        {
        }
    }
}
