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
using System.IO;

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
        internal static MainPage Instance { get; set; }

        /// <summary>
        /// Basic constructor for MainPage[Till]
        /// initalises Basket and StoredTrans
        /// gets BagItem if exist in DB and choses page view based on BagItem
        /// </summary>
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

            var tempIList = App.DbContext.Search("BAG001").ToList();
            if (tempIList.Count != 1) return;
            BagItem = tempIList.First();
            Bag.Text = BagItem.Name;
            Bag.IsVisible = true;
            CountBasketNum = 1;
            Instance = this;
        }

        /// <summary>
        /// Show item information with ItemTemplate page 
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        async void Handle_ItemTapped(object sender, SelectedItemChangedEventArgs e)
        {
            if (e.SelectedItem == null)
                return;
            var temp = e.SelectedItem as Basket;
            if(!Authorisation.IsAuthorised("Item", "V", App.LastAuthUser))
            {
                if (App.EmpsLogged.Count > 1)
                    await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
                Action action = async () =>
                {
                    await Navigation.PushModalAsync(new Inventory.ItemTemplate(temp));
                    ((ListView)sender).SelectedItem = null;
                };
                Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Item", "V", Basket.Where(item => item.Return.Equals(true)).Sum(item => item.Price * item.Amount), action);
            }
            else
            {
                await Navigation.PushModalAsync(new Inventory.ItemTemplate(temp));
                ((ListView)sender).SelectedItem = null;
            }
        }

        /// <summary>
        /// run through all items in Basket
        /// where itemId, Return and SaleID are same remove the whole item from the Basket
        /// </summary>
        /// <param name="item">Item thats to be removed</param>
        private void Remove(Basket item)
        {
            for(int i = 0; i <= Basket.Count - 1; i++)
            {
                if (Basket[i].Id == item.Id && Basket[i].Return == item.Return && Basket[i].SaleId == item.SaleId)
                {
                    Basket.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Get the item that needs to be removed as Basket type, then run Remove
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private void OnDelete(object sender, EventArgs e)
        {
            var menuItem = (Basket)((MenuItem)sender).CommandParameter;
            Remove(menuItem);
        }

        /// <summary>
        /// run through all items in Basket
        /// where itemId, Return and SaleID are same take one of amount and then run Update price if the item is not fully removed from Basket
        /// Fully remove item from Basket if amount reaches 0
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private void OnRemove1(object sender, EventArgs e)
        {
            var menuItem = (Basket)((MenuItem)sender).CommandParameter;
            for (int i = 0; i <= Basket.Count - 1; i++)
            {
                if (Basket[i].Id == menuItem.Id && Basket[i].Return == menuItem.Return && Basket[i].SaleId == menuItem.SaleId)
                {
                    Basket[i].Amount--;
                    if (Basket[i].Amount == 0)
                        Basket.RemoveAt(i);
                    else
                        UpdatePrice();
                }
            }
        }

        /// <summary>
        /// Open mobile scanner and if item exist in DB run BasketAdd and send item id 
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
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
                    var tempIList = App.DbContext.Search(result.Text).ToList();
                    if (tempIList.Count != 1) return;
                    var tempI = new Basket(tempIList.First());
                    BasketAdd(tempI);
                });
            };

            await Navigation.PushAsync(_scanPage);
        }

        /// <summary>
        /// Calculate Price, ExVat Price and display
        /// </summary>
        public void UpdatePrice()
        {
            var price = Basket.Sum(item => item.Price * item.Amount);
            PriceCell.Text = price.ToString();
            var ExPrice = Basket.Sum(item => (item.Price * (1.0m - (decimal)(item.Vat.Rate - 1)) * item.Amount));
            PriceExVCell.Text = Math.Round(ExPrice, 2, MidpointRounding.AwayFromZero).ToString();
        }

        /// <summary>
        /// Display are you sure, if yes the clear whole basket
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private void Cancel_Clicked(object sender, EventArgs e)
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                var quit = await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("CancelTransaction_Mesg"), App.Translate.ProvideValue("Yes"), App.Translate.ProvideValue("Cancel"));

                if (!quit) return;
                Basket.Clear();
            });
        }

        /// <summary>
        /// Check if employee is authorised to run Till
        /// also ask what payment method with actionsheet
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private async void COut_Clicked(object sender, EventArgs e)
        {
            if (Basket.Count == 0)
            {
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("BasketEmpty"), App.Translate.ProvideValue("OK"));
                return;
            }

            Action action = () =>
            {
                GenTransaction();
            };
            Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Till", "X", action);            
        }
        
        /// <summary>
        /// Create PaymentSaleModal, SaleModal.
        /// initalise all lists on SaleModal, add PaymentSaleModal to SaleModal.PaySale
        /// run through all items in Basket if item.Return is false then create transaction for that item and add it to SaleModal.Transactions,
        /// if item.Return is true then create a RefundModal with that item and then add to SaleModal.Refunds
        /// then add all to DB and save
        /// then clear Basket
        /// </summary>
        /// <param name="Action">Selected paymethod</param>
        private async void GenTransaction()
        {
            var actionDic = new Dictionary<string, Func<PaymentMethodModel>> {
                { App.Translate.ProvideValue("Card"), () => App.DbContext.GetPayM(App.Translate.ProvideValue("Card")).SingleOrDefault() },
                { App.Translate.ProvideValue("Cash"), () => App.DbContext.GetPayM(App.Translate.ProvideValue("Cash")).SingleOrDefault() },
                { App.Translate.ProvideValue("Cancel"), null}
            };

            EId.Text = null;
            VerifyId.IsVisible = false;
            MPage.IsEnabled = true;

            decimal Total = 0.0m;
            decimal Change = 0.0m;

            //GetCustomer Data here

            SaleModel Sale = new SaleModel() { DateOfSale = DateTime.Now, Total = Total, EmployeeId = App.LastAuthUser.Id };
            Sale.PaySales = new List<PaymentMethod_SaleModel>();
            Sale.Notes = new List<Notes_SaleModel>();

            if (Basket.Where(item => item.Return.Equals(false)).Count() == 0)
            {
                //add discount for when only a refund is being processed for the paymentcharge amount
            }
            else
            {
                Total = Basket.Sum(item => item.Price * item.Amount);
                for (decimal paid = 0.0m; paid < Total;)
                {
                    string Action = null;
                    Action = await DisplayActionSheet(App.Translate.ProvideValue("PayMeth"), App.Translate.ProvideValue("Cancel"), null, App.Translate.ProvideValue("Card"), App.Translate.ProvideValue("Cash"));

                    if (Action == App.Translate.ProvideValue("Cancel"))
                    {
                        App.DbContext.RevertDbContextChanges();
                        return;
                    }

                    PaymentMethod_SaleModel pay = new PaymentMethod_SaleModel() { PayMethod = actionDic[Action]() };

                    if (pay.PayMethod.MinimumCharge > Total)
                    {
                        Total += pay.PayMethod.Charge;
                        NoteModel note = App.DbContext.GetNote(string.Format(App.Translate.ProvideValue("CardChargeNote"), pay.PayMethod.Charge));
                        if (note == null)
                        {
                            note = new NoteModel() { Note = string.Format(App.Translate.ProvideValue("CardChargeNote"), pay.PayMethod.Charge) };
                            App.DbContext.Add(note);
                        }
                        Notes_SaleModel NotesSale = new Notes_SaleModel() { Note = note };
                        App.DbContext.Add(NotesSale);
                        Sale.Notes.Add(NotesSale);
                    }

                    var amount = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(string.Format(App.Translate.ProvideValue("HowMuchPM"), Action, (Total-paid).ToString()), "enter here", "confrim", "shit");

                    pay.Amount = amount;

                    paid += amount;

                    if (paid > Total)
                    {
                        if (pay.PayMethod.IsChangeable)
                        {
                            Change = paid - Total;
                        }
                        else
                        {
                            paid -= amount;
                            continue;
                        }
                    }
                    App.DbContext.Add(pay);
                    Sale.PaySales.Add(pay);
                }
                Sale.Total = Total;
            }
            
            foreach (var pay in Sale.PaySales)
            {
                if (pay.PayMethod.IsCashBackable)
                {
                    var Cashback = await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("CashBack_"), App.Translate.ProvideValue("Yes"), App.Translate.ProvideValue("Cancel"));
                    if (Cashback)
                    {
                        var amount = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(App.Translate.ProvideValue("HowMuchCB"), "enter here", "confrim", "shit");
                        if (amount > 0.01m)
                        {
                            NoteModel CB = App.DbContext.GetNote(string.Format(App.Translate.ProvideValue("CBNote"), amount));
                            if(CB == null)
                            {
                                CB = new NoteModel { Note = string.Format(App.Translate.ProvideValue("CBNote"), amount) };
                                App.DbContext.Add(CB);
                            }
                            Notes_SaleModel CBNSale = new Notes_SaleModel { Note = CB };
                            App.DbContext.Add(CBNSale);
                            Sale.Notes.Add(CBNSale);
                            Change += amount;
                            Total += amount;
                        }
                    }
                    break;
                }
            }

            var Continue = await DisplayAlert(App.Translate.ProvideValue("Hmm"), String.Format(App.Translate.ProvideValue("Continue"), Total), App.Translate.ProvideValue("Yes"), App.Translate.ProvideValue("Cancel"));
            if (!Continue)
            {
                App.DbContext.RevertDbContextChanges();
                return;
            }

            Sale.Transactions = new List<TransactionModel>();
            Sale.Refunds = new List<RefundModel>();

            foreach (var item in Basket)
            {
                ItemModel Item = new ItemModel(item);
                if (item.Return)
                {
                    RefundModel Refund = new RefundModel() { ItemId = item.Id, Sale = Sale, SaleIdReturned = item.SaleId, Reason = item.Reason, Amount = item.Amount };
                    Sale.Refunds.Add(Refund);
                    App.DbContext.Add(Refund);
                }
                else
                {
                    TransactionModel Tran = new TransactionModel() { Item = Item, Sale = Sale, Amount = item.Amount };
                    Sale.Transactions.Add(Tran);
                    App.DbContext.Add(Tran);
                }
            }

            if (Sale.Refunds.Count == 0)
                FinaliseTransaction(Sale, Change);
            else if(!Authorisation.IsAuthorised("Refund", "X", Basket.Where(item=>item.Return.Equals(true)).Sum(item=>item.Price*item.Amount), App.LastAuthUser))
            {
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("RefundLimitTooLow"), App.Translate.ProvideValue("OK"));
                Action action = () => FinaliseTransaction(Sale, Change);
                Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Refund", "X", Basket.Where(item => item.Return.Equals(true)).Sum(item => item.Price * item.Amount), action);
            }
            else
                FinaliseTransaction(Sale, Change);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Sale"></param>
        private async void FinaliseTransaction(SaleModel Sale, decimal cashBack)
        {
            if (cashBack != 0.0m)
            {
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), string.Format(App.Translate.ProvideValue("CashBack"), cashBack), App.Translate.ProvideValue("OK"));
            }

            App.DbContext.Add(Sale);

            if (!App.DbContext.Save())
            {
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("DbIssue"), App.Translate.ProvideValue("OK"));
                return;
            }

            PDFCreator pdf = new PDFCreator(App.Store, null, Sale, cashBack);
            
            Basket.Clear();
            await DisplayAlert(App.Translate.ProvideValue("Transaction"), App.Translate.ProvideValue("TransConfMesg"), App.Translate.ProvideValue("OK"));
        }

        /// <summary>
        /// On ManScan complete
        /// get item from DB if only one returns run AddBasket with item passed as Basket else throw error message
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private async void ManScan_Completed(object sender, EventArgs e)
        {
            var tempIList = App.DbContext.Search(ManScan.Text).ToList();
            if (tempIList == null || tempIList.Count != 1)
            {
                ManScan.Text = null;
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("ItemIdNotFoundMesg"), App.Translate.ProvideValue("OK"));
                return;
            }
            ManScan.Text = null;
            //var s = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
            //Debug.WriteLine(s);
            var tempI = new Basket(tempIList.First());
            tempIList.Clear();
            BasketAdd(tempI);
            tempI = null;
        }

        /// <summary>
        /// Run BasketAdd and send BagItem to it
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private void Bag_Clicked(object sender, EventArgs e)
        {
            BasketAdd(new Basket(BagItem));
        }

        /// <summary>
        /// run through all items in Basket if item.itemid, item.Return and Sale.SaleId are the same then increament amount of the item up 1 and update price,
        /// else add the item to Basket as new 
        /// </summary>
        /// <param name="tempItem">Item to add to Basket</param>
        private async void BasketAdd(Basket tempItem)
        {
            var amount = (int)await Conversions.ToInterger(AmountToAdd.Text, App.Translate.ProvideValue("ValueEnteredWrong"));
            if (amount == -1)
                return;
            foreach (var item in Basket)
            {
                if (item.Id != tempItem.Id) continue;
                if (item.Return != tempItem.Return) continue;
                if (item.SaleId != tempItem.SaleId) continue;
                item.Amount=item.Amount+amount;
                UpdatePrice();
                return;
            }
            Basket.Add(tempItem);
            Basket.Last().Amount = amount;
            UpdatePrice();
            AmountToAdd.Text = 1.ToString();
        }

        /// <summary>
        /// check if Bsket is empty if it is escape else add current basket to StoredTrans
        /// then clear Basket, run CheckStoreTransExist and increament CountBasketNum
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private void StoreTrans_Clicked(object sender, EventArgs e)
        {
            if (Basket.Count == 0) return;
            StoredTrans.Add(CountBasketNum, new ObservableCollection<Basket>(Basket));
            Basket.Clear();
            CheckStoreTransExist();
            CountBasketNum++;
        }

        /// <summary>
        /// Check if StoredTrans has one item if it does run through ToolBarItems check if Baskets already exists if it does escape else create it
        /// if StoredTrans has zero items then remove Baskets from ToolBarItems
        /// </summary>
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

        /// <summary>
        /// Push the BasketsListPage
        /// </summary>
        private async void ShowBasketList()
        {
            if (StoredTrans.Count == 1)
            {
                FetchSavedBasket(StoredTrans.Single(), Instance);
            }
            else
            {
                await Navigation.PushAsync(new BasketListPage());
            }
        }

        /// <summary>
        /// Get item Clicked and Push ReturnFormPage sending item with it
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private async void OnReturn(object sender, EventArgs e)
        {
            var menuItem = (Basket)((MenuItem)sender).CommandParameter;
            Basket Item = null;
            foreach (var item in Basket)
            {
                if (item.Id == menuItem.Id && item.Return == menuItem.Return && item.SaleId == menuItem.SaleId)
                {
                    Item = new Models.Basket(item);
                }
            }
            await Navigation.PushAsync(new ReturnFormPage(Item));
        }

        /// <summary>
        /// Check if Basket is empty if not then ask if you want to replace it if yes clear basket else leave it,
        /// add all items from selectedBasket to current Basket, then remove selectedBasket from StoredTrans
        /// then reorder all StoredTrans
        /// CountBasketNum increment -1
        /// then run CheckStoreTransExist
        /// </summary>
        /// <param name="selectedBasket">Basket Selected from BasketListPage</param>
        /// <param name="page">Current page instance to access non static methods and variables</param>
        internal static void FetchSavedBasket(KeyValuePair<int, ObservableCollection<Basket>> selectedBasket, MainPage page)
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                if (page.Basket.Count > 0)
                {
                    var quit = await page.DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("BasketReplace"), App.Translate.ProvideValue("Yes"), App.Translate.ProvideValue("Cancel"));

                    if (quit)
                    {
                        page.Basket.Clear();
                    }
                    else
                    {
                        return;
                    }
                }
                foreach (var item in selectedBasket.Value)
                {
                    page.BasketAdd(item);
                }
                StoredTrans.Remove(selectedBasket.Key);
                if (StoredTrans.Count > 0)
                {
                    for (int i = 1; i <= StoredTrans.Last().Key; i++)
                    {
                        if (i > selectedBasket.Key)
                        {
                            var itemKey = i;
                            ObservableCollection<Basket> itemData = StoredTrans[i];
                            itemKey = itemKey - 1;
                            StoredTrans.Remove(i);
                            StoredTrans.Add(itemKey, itemData);
                        }
                    }
                }
                page.CountBasketNum--;
                page.CheckStoreTransExist();
            });
        }

        /// <summary>
        /// Remove oldItem from Basket
        /// then add newItem to Basket
        /// </summary>
        /// <param name="oldItem">Item to remove from Basket</param>
        /// <param name="newItem">Item to add to Basket</param>
        /// <param name="page">Current page instance to access non static methods and variables</param>
        internal static void ReturnListener(Basket oldItem, Basket newItem, MainPage page)
        {
            page.Remove(oldItem);
            page.Basket.Add(newItem);
        }

        private void CancelEmpCheck_Clicked(object sender, EventArgs e)
        {
            EId.Text = null;
            VerifyId.IsVisible = false;
            MPage.IsEnabled = true;
            App.DbContext.RevertDbContextChanges();
        }
    }
}