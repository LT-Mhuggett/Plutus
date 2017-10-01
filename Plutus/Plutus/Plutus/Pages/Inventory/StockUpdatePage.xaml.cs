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

        /// <summary>
        /// Basic constructor for stockUpdatePage
        /// </summary>
        public StockUpdatePage ()
		{
			InitializeComponent ();

            if (Device.Idiom == TargetIdiom.Desktop)
                ScanButt.IsVisible = false;
		}

        public StockUpdatePage(ItemModel item)
        {
            InitializeComponent();

            ScanButt.IsVisible = false;
            EnterButt.IsVisible = false;

            Id.Text = item.Id;
            Id.IsEnabled = false;
            Confirm.IsEnabled = true;
        }

        /// <summary>
        /// create Stock model and add to DB and save if all values are not null
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
        private async void Confirm_Clicked(object sender, EventArgs e)
        {
            StockModel stock = new StockModel
            {
                StoreId = App.Store.Id,
                ItemId = String.IsNullOrWhiteSpace(Id.Text) ? null : Id.Text,
                Quantity = Convert.ToInt16(Quantity.Text)
            };

            if (stock.Quantity < 1)
            {
                return;
            }

            App.DbContext.UpdateStock(stock);
            if (!App.DbContext.Save())
            {
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("DbIssue"), App.Translate.ProvideValue("OK"));
                return;
            }
            await Navigation.PopToRootAsync();
        }

        /// <summary>
        /// Check if item already exist if it does then in lock Confirm button
        /// </summary>
        private async void CheckExist()
        {
            if (!App.DbContext.IsIdSame<ItemModel, string>(Id.Text))
            {
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("ItemNonExistMesg"), App.Translate.ProvideValue("OK"));
                Id.Text = null;
                Confirm.IsEnabled = false;
            }
            else
                Confirm.IsEnabled = true;
        }

        /// <summary>
        /// Run CheckExist on Id complete
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
        private void Id_Completed(object sender, EventArgs e)
        {
            CheckExist();
        }

        /// <summary>
        /// Open mobile scanner
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
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

        private void  EnterButt_Clicked(object sender, EventArgs e)
        {
            CheckExist();
        }
    }
}