using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Plutus.Helpers;
using Plutus.Helpers.Interface;

namespace Plutus.Pages.Admin
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class MainPage : ContentPage
	{
		public MainPage ()
		{
			InitializeComponent ();

            VersionLabel.Text = string.Format("Version: Beta {0}", App.Version);
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
                    await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Success"), string.Format(App.Translate.ProvideValue("DbBRSucc"), "Restored"), App.Translate.ProvideValue("OK"));
                    App.DbContext = new Helpers.Database(App.AppSettings.DatabaseProvider);
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

        private async void DeleteDBButt_Clicked(object sender, EventArgs e)
        {
            var quit = await DisplayAlert(App.Translate.ProvideValue("Hmm"), "Are you sure you want to Delete the DB?(This is for quick testing only), App will shutdown after.", App.Translate.ProvideValue("Yes"), App.Translate.ProvideValue("Cancel"));

            if (!quit) return;
            File.Delete(Path.Combine(FileIO.GetLib(), "Database.db"));
            App.AppSettings.DatabaseProvider = null;
            var closer = DependencyService.Get<ICloseApp>();
            if (closer == null) return;
            App.EmpsLogged = null;
            closer.CloseApp();
        }

        private void ChangePrinter_Clicked(object sender, EventArgs e)
        {
            Action action = async () =>
            {
                var printerMgr = new PosPrinterManager();
                var printerList = await printerMgr.GetPrinterList();

                if (printerList.Count > 0)
                {
                    var result = await App.Current.MainPage.DisplayActionSheet(App.Translate.ProvideValue("PrinterList_"), App.Translate.ProvideValue("Cancel"), null, printerList.Keys.ToArray());
                    if (result != App.Translate.ProvideValue("Cancel"))
                    {
                        App.AppSettings.PrinterLogicalName = result;
                    }
                    else
                    {
                        await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Warning"), App.Translate.ProvideValue("NoPrinter"), App.Translate.ProvideValue("Cancel"));
                        App.AppSettings.PrinterLogicalName = null;
                    }
                }
                else
                {
                    await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Warning"), App.Translate.ProvideValue("NoPrinter"), App.Translate.ProvideValue("Cancel"));
                }
            };
            Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Admin", "X", action);
        }

        private void AddPayMeth_Clicked(object sender, EventArgs e)
        {
            Action action = async () => await App.Current.MainPage.Navigation.PushAsync(new AddPaymentMeth());
            Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Admin", "X", action);
        }
    }
}