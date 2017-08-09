using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Helpers;
using Plutus.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using I18N_L10N;
using ZXing.Net.Mobile.Forms;
using ZXing.Mobile;

namespace Plutus.Pages.Inventory
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class StockUpdatePage : ContentPage
	{
        private ZXingScannerPage _scanPage;

        public StockUpdatePage ()
		{
			InitializeComponent ();
		}

        private async void Confirm_Clicked(object sender, EventArgs e)
        {
            StockModel stock = new StockModel
            {
                StoreId = App.Store.StoreId,
                ItemId = Id.Text,
                Quantity = Convert.ToInt16(Quantity.Text)
            };

            App.DbContext.UpdateStock(stock);
            App.DbContext.Save();
            await Navigation.PopAsync();
        }

        private async void CheckExist()
        {
            if (!App.DbContext.IsIdSame(Id.Text))
            {
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("ItemNonExistMesg"), App.Translate.ProvideValue("OK"));
                Id.Text = null;
            }
        }

        private void Id_Completed(object sender, EventArgs e)
        {
            CheckExist();
        }

        private async void ScanButt_Clicked(object sender, EventArgs e)
        {
            var opt = new MobileBarcodeScanningOptions
            {
                UseNativeScanning = true,
                TryHarder = true,
                TryInverted = true
            };
            _scanPage = new ZXingScannerPage(opt, null);
            _scanPage.OnScanResult += (result) =>
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    Navigation.PopAsync();
                    Id.Text = result.Text;
                    CheckExist();
                });
            };
            await Navigation.PushAsync(_scanPage);
        }
    }
}