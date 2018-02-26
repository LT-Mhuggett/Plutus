using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dropbox.Api;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Plutus.Helpers;

namespace Plutus.Pages.Admin
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class MainPage : ContentPage
	{
		public MainPage ()
		{
			InitializeComponent ();

            VersionLabel.Text = string.Format("Version: Beta", App.Version);
		}

        private void Backup_Clicked(object sender, EventArgs e)
        {
            Action action = async () =>
            {
                var TransfSucc = await FileIO.BackUp();

                if (TransfSucc)
                {
                    await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Success"), string.Format(App.Translate.ProvideValue("DbBRSucc"), "Backed Up"), App.Translate.ProvideValue("Cancel"));
                    return;
                }
                await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Hmm"), string.Format(App.Translate.ProvideValue("DbBRFailed"), "Backing Up"), App.Translate.ProvideValue("Cancel"));
            };
            Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Admin", "X", action);
        }

        private void Restore_Clicked(object sender, EventArgs e)
        {
            Action action = async () =>
            {
                App.DbContext = null;
                var TransfSucc = await FileIO.Restore();

                if (TransfSucc)
                {
                    await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Success"), string.Format(App.Translate.ProvideValue("DbBRSucc"), "Restored"), App.Translate.ProvideValue("Cancel"));
                    App.DbContext = new Database();
                    return;
                }
                await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Hmm"), string.Format(App.Translate.ProvideValue("DbBRFailed"), "Restoring"), App.Translate.ProvideValue("Cancel"));
            };
            Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Admin", "X", action);
        }

        private void CancelEmpCheck_Clicked(object sender, EventArgs e)
        {
            EId.Text = null;
            VerifyId.IsVisible = false;
            MPage.IsEnabled = true;
        }
    }
}