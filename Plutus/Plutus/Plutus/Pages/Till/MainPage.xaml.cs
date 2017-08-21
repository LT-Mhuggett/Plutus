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
        public static Dictionary<int, ObservableCollection<Basket>> StoredTrans { get; set; }
        public ItemModel BagItem { get; set; }
        private ZXingScannerPage _scanPage;
        private int CountBasketNum { get; set; }
        private bool Sub { get; set; }

        public MainPage()
        {
            InitializeComponent();

            if(Basket == null)
                Basket = new ObservableCollection<Basket>();
            if(StoredTrans == null)
                StoredTrans = new Dictionary<int, ObservableCollection<Basket>>();

            if (Device.Idiom == TargetIdiom.Desktop)
            {
                Scan.IsVisible = false;
                ManScan.IsVisible = true;
                ManScan.Focus();
            }

            PriceCell.Text = "0.00";
            PriceExVCell.Text = "0.00";

            Basket.CollectionChanged += (e, v) => UpdatePrice();

            BindingContext = this;

            var tempIList = App.DbContext.GetItem("BAG001");
            if (tempIList.Count != 1) return;
            BagItem = new Basket(tempIList.First());
            Bag.Text = BagItem.Name;
            Bag.IsVisible = true;
            CountBasketNum = 1;
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
                    BasketAdd(tempI);
                });
            };

            await Navigation.PushAsync(_scanPage);
        }

        public void UpdatePrice()
        {
            var price = Basket.Sum(item => item.Price * item.Amount);
            PriceCell.Text = price.ToString();
            var ExPrice = Basket.Sum(item => (item.Price * (1.0m - (decimal)(item.Vat.Rate - 1)) * item.Amount));
            PriceExVCell.Text = Math.Round(ExPrice, 2, MidpointRounding.AwayFromZero).ToString();
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

        private async void COut_Clicked(object sender, EventArgs e)
        {
            if (!Authorisation.IsAuthorised("Till"))
            {
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
                return;
            }

            if (Basket.Count == 0)
            {
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("BasketEmpty"), App.Translate.ProvideValue("OK"));
                return;
            }

            string Action;

            Action = await DisplayActionSheet(App.Translate.ProvideValue("PayMeth"), App.Translate.ProvideValue("Cancel"), null, App.Translate.ProvideValue("Card"), App.Translate.ProvideValue("Cash"));


            
            GenTransaction(Action);
        }

        private async void GenTransaction(string Action)
        {
            
            var actionDic = new Dictionary<string, Func<PaymentMethodModel>> {
                { App.Translate.ProvideValue("Card"), () => App.DbContext.GetPayM(App.Translate.ProvideValue("Card"))},
                { App.Translate.ProvideValue("Cash"), () => App.DbContext.GetPayM(App.Translate.ProvideValue("Cash")) },
                { App.Translate.ProvideValue("Cancel"), null}
            };

            /*            
            EmployeeModel emp = new EmployeeModel();
            
            if (Device.Idiom == TargetIdiom.Desktop)
            {

            }

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
                    emp = App.EmpsLogged.Where(i => i.Id.Equals(result.Text)).FirstOrDefault();
                });
            };
            await Navigation.PushAsync(_scanPage);
            */
            EmployeeModel emp = App.EmpsLogged.First();
            if (emp == null) return;

            PaymentMethod_SaleModel paySale = new PaymentMethod_SaleModel() { PayMethod = actionDic[Action]() };
            var Total = Basket.Sum(item => item.Price * item.Amount) + paySale.PayMethod.Charge;
            var Continue = await DisplayAlert(App.Translate.ProvideValue("Hmm"), String.Format(App.Translate.ProvideValue("Continue?"), Total), App.Translate.ProvideValue("Yes"), App.Translate.ProvideValue("Cancel"));

            if (!Continue) return;

            SaleModel Sale = new SaleModel() { DateOfSale = System.DateTime.Now, Total = Total, EmployeeId = emp.Id };
            Sale.PaySales = new List<PaymentMethod_SaleModel>();
            Sale.Transactions = new List<TransactionModel>();
            Sale.PaySales.Add(paySale);

            foreach (var item in Basket)
            {
                ItemModel Item = new ItemModel(item);
                TransactionModel Tran = new TransactionModel() { Item = Item, Sale = Sale, Amount = item.Amount };
                Sale.Transactions.Add(Tran);
                App.DbContext.Add(Tran);
            }
            App.DbContext.Add(Sale);
            App.DbContext.Add(paySale);

            App.DbContext.Save();
            Basket.Clear();
            await DisplayAlert(App.Translate.ProvideValue("Transaction"), App.Translate.ProvideValue("TransConfMesg"), App.Translate.ProvideValue("OK"));
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
            BasketAdd(tempI);
        }

        private void Bag_Clicked(object sender, EventArgs e)
        {
            BasketAdd(new Basket(BagItem));
        }

        private void BasketAdd(Basket tempItem)
        {
            foreach (var item in Basket)
            {
                if (item.ItemId != tempItem.ItemId) continue;
                item.Amount++;
                UpdatePrice();
                return;
            }
            Basket.Add(tempItem);
        }

        private void StoreTrans_Clicked(object sender, EventArgs e)
        {
            if (Basket.Count == 0) return;
            StoredTrans.Add(CountBasketNum, new ObservableCollection<Basket>(Basket));
            Basket.Clear();
            CheckStoreTransExist();
            CountBasketNum++;
        }

        private void CheckStoreTransExist()
        {
            if (StoredTrans.Count == 1)
            {
                foreach(var item in App.Current.MainPage.ToolbarItems)
                {
                    if(item.Text == App.Translate.ProvideValue("Baskets"))
                        return;
                }
                App.Current.MainPage.ToolbarItems.Add(new ToolbarItem
                {
                    Text=App.Translate.ProvideValue("Baskets"),
                    Icon="",
                    Command=new Command(this.ShowBasketList)
                });
            }
            else if (StoredTrans.Count == 0)
            {
                App.Current.MainPage.ToolbarItems.Clear();
            }
        }

        private async void ShowBasketList()
        {
            await Navigation.PushAsync(new BasketListPage());
            if (!Sub)
            {
                MessagingCenter.Subscribe<MainPage, KeyValuePair<int, ObservableCollection<Basket>>>(this, "BasketData", (Sender, arg) =>
                {
                    Device.BeginInvokeOnMainThread(async () =>
                    {
                        if (Basket.Count > 0)
                        {

                            var quit = await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("BasketReplace?"), App.Translate.ProvideValue("Yes"), App.Translate.ProvideValue("Cancel"));

                            if (quit)
                            {
                                Basket.Clear();
                            }
                            else
                            {
                                return;
                            }
                        }
                        foreach (var item in arg.Value)
                        {
                            BasketAdd(item);
                        }
                        StoredTrans.Remove(arg.Key);
                        if (StoredTrans.Count > 0)
                        {
                            for (int i = 1; i <= StoredTrans.Last().Key; i++)
                            {
                                if (i > arg.Key)
                                {
                                    var itemKey = i;
                                    ObservableCollection<Basket> itemData = StoredTrans[i];
                                    itemKey = itemKey - 1;
                                    StoredTrans.Remove(i);
                                    StoredTrans.Add(itemKey, itemData);
                                }
                            }
                        }
                        CountBasketNum--;
                        CheckStoreTransExist();
                    });
                });
                Sub = true;
            }
        }
    }
}