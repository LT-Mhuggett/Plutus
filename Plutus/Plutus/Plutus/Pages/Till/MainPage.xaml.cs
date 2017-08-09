using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Plugin.MediaManager;
using Plutus.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Plutus.Helpers;
using ZXing.Net.Mobile.Forms;
using ZXing.Mobile;

namespace Plutus.Pages.Till
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class MainPage : ContentPage
    {
        public ObservableCollection<Basket> Basket { get; set; }
        private ZXingScannerPage _scanPage;

        public MainPage()
        {
            InitializeComponent();

            Basket = new ObservableCollection<Basket>();

            if (Device.Idiom == TargetIdiom.Desktop)
            {
                Scan.IsVisible = false;
                ManScan.IsVisible = true;
                ManScan.Focus();
            }

            PriceCell.Text = "0";

            Basket.CollectionChanged += (e, v) => UpdatePrice();

            BindingContext = this;
        }

        async void Handle_ItemTapped(object sender, SelectedItemChangedEventArgs e)
        {
            if (e.SelectedItem == null)
                return;
            var temp = e.SelectedItem as Basket;

            await Navigation.PushModalAsync(new Inventory.ItemTemplate(temp));
            ((ListView)sender).SelectedItem = null;
        }

        private void OnDelete(object sender, EventArgs e)
        {
            var menuItem = (Basket)((MenuItem) sender).CommandParameter;
            Basket.Remove(menuItem);
        }

        private void OnRemove1(object sender, EventArgs e)
        {
            var menuItem = (Basket)((MenuItem)sender).CommandParameter;
            var temp = Basket.Where(i=>i.ItemId.Equals(menuItem.ItemId));
            temp.First().Amount--;
            if (temp.First().Amount == 0)
                Basket.Remove(menuItem);
            else
                UpdatePrice();
        }

        private async void Scan_Onclicked(object sender, EventArgs e)
        {
            var opt = new MobileBarcodeScanningOptions
            {
                DelayBetweenContinuousScans = 3000,
                UseNativeScanning = true,
                TryHarder = true,
                TryInverted = true
            };
            _scanPage = new ZXingScannerPage(opt, null);
            _scanPage.OnScanResult += (result) =>
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    var tempIList = App.DbContext.GetItem(result.Text);
                    if (tempIList.Count != 1) return;
                    //var s = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
                    //Debug.WriteLine(s);
                    var tempI = new Basket(tempIList.First());
                    foreach (var item in Basket)
                    {
                        if (item.ItemId != tempI.ItemId) continue;
                        item.Amount++;
                        UpdatePrice();
                        return;
                    }
                    Basket.Add(tempI);
                });
            };

            await Navigation.PushAsync(_scanPage);
        }

        public void UpdatePrice()
        {
            var price = Basket.Sum(item => item.Price * item.Amount);
            PriceCell.Text = price.ToString();
        }

        private void Cancel_Clicked(object sender, EventArgs e)
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                var quit = await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("CancelTransaction?Mesg"), App.Translate.ProvideValue("Yes"), App.Translate.ProvideValue("Cancel"));

                if (!quit) return;
                Basket.Clear();
            });
        }

        private void COut_Clicked(object sender, EventArgs e)
        {

        }

        private async void ManScan_Completed(object sender, EventArgs e)
        {
            var tempIList = App.DbContext.GetItem(ManScan.Text);
            if (tempIList.Count != 1)
            {
                ManScan.Text = null;
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("ItemIdNotFoundMesg"), App.Translate.ProvideValue("OK"));
                return;
            }
            ManScan.Text = null;
            //var s = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
            //Debug.WriteLine(s);
            var tempI = new Basket(tempIList.First());
            foreach (var item in Basket)
            {
                if (item.ItemId != tempI.ItemId) continue;
                item.Amount++;
                UpdatePrice();
                return;
            }
            Basket.Add(tempI);
        }
    }
}