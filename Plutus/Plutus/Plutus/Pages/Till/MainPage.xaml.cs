using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Plutus.Models;
using Plutus.Helpers.Extensions;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Plutus.Helpers;
using ZXing.Net.Mobile.Forms;
using ZXing.Mobile;
using Microsoft.EntityFrameworkCore;

namespace Plutus.Pages.Till
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class MainPage : ContentPage
    {
        public ObservableCollection<Basket> Basket { get; set; }
        public static Dictionary<int, ObservableCollection<Basket>> StoredTrans { get; private set; }
        private ItemModel BagItem { get; }
        private ZXingScannerPage _scanPage;
        private int _countBasketNum { get; set; }
        internal static MainPage Instance { get; private set; }

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
            if (tempIList.Count < 1)
            {
                var cat = new CategoryModel() {Name = "Customer Care", Description = "Items such as Bags etc."};
                App.DbContext.Add(cat);
                var bag = new ItemModel()
                {
                    Id = "BAG001",
                    Name = "Bag",
                    Desc = "Item to allow Customers to carry things",
                    CatId = 2,
                    VatId = 3,
                    Price = .05m,
                    Cost = 0.0m
                };
                App.DbContext.Add(bag);
                App.DbContext.Save();
                BagItem = bag;
            }
            else
                BagItem = tempIList.First();
            Bag.Text = BagItem.Name;
            Bag.IsVisible = true;
            
            _countBasketNum = 1;
            Instance = this;

            MessagingCenter.Subscribe<App, ItemModel>((App)Application.Current, "AddItemToBasket", (sender, arg) =>
                {
                    var tempItem = new Basket(arg);
                    BasketAdd(tempItem);
                });
        }

        /// <summary>
        /// Show item information with ItemTemplate page 
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private async void Handle_ItemTapped(object sender, ItemTappedEventArgs e)
        {
            if (e.Item == null)
                return;
            var temp = e.Item as Basket;
            if(!Authorisation.IsAuthorised("Item", "V", App.LastAuthUser))
            {
                if (App.EmpsLogged.Count > 1)
                    await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));

                async void Action()
                {
                    await Navigation.PushModalAsync(new Inventory.ItemTemplate(temp));
                    ((ListView) sender).SelectedItem = null;
                }

                Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Item", "V", Basket.Where(item => item.Return.Equals(true)).Sum(item => item.Price * item.Amount), Action);
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
            for(var i = 0; i <= Basket.Count - 1; i++)
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
            for (var i = 0; i <= Basket.Count - 1; i++)
            {
                if (Basket[i].Id != menuItem.Id || Basket[i].Return != menuItem.Return ||
                    Basket[i].SaleId != menuItem.SaleId) continue;
                Basket[i].Amount--;
                if (Basket[i].Amount == 0)
                    Basket.RemoveAt(i);
                else
                    UpdatePrice();
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
                    var tempItem = FindItem(result.Text);
                    if (tempItem == null) return;
                    BasketAdd(new Basket(tempItem));
                });
            };

            await Navigation.PushAsync(_scanPage);
        }

        /// <summary>
        /// Calculate Price, ExVat Price and display
        /// </summary>
        private void UpdatePrice()
        {
            var price = Basket.Sum(item => (item.Price * item.Amount));
                PriceCell.Text = Math.Round(price, 2, MidpointRounding.AwayFromZero).ToString();
            var exPrice = Basket.Sum(item =>
                item.ExPrice * item.Amount);
            PriceExVCell.Text = Math.Round(exPrice, 2, MidpointRounding.AwayFromZero).ToString();
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

            void Action()
            {
                GenTransaction();
            }

            Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Till", "X", Action);            
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
            var actionDic = App.DbContext.Get<PaymentMethodModel>().OrderBy(p => p.Name)
                .ToDictionary<PaymentMethodModel, string, Func<PaymentMethodModel>>(tempPayMeth => tempPayMeth.Name,
                    tempPayMeth => (() => tempPayMeth));
            actionDic.Add(App.Translate.ProvideValue("Cancel"), null);

            EId.Text = null;
            VerifyId.IsVisible = false;
            MPage.IsEnabled = true;

            var total = 0.0m;
            var change = 0.0m;

            //GetCustomer Data here

            var sale = new SaleModel
            {
                DateOfSale = DateTime.Now,
                Total = total,
                EmployeeId = App.LastAuthUser.Id,
                PaySales = new List<PaymentMethod_SaleModel>(),
                Notes = new List<Notes_SaleModel>()
            };

            var refundOnly = !Basket.Any(item => item.Return.Equals(false));
            
            var discountAmount = 0.0m;
            var discountsUsed = new List<DiscountModel>();

            foreach (var item in Basket)
            {
                var disItems = new List<Discount_Item>();
                var disCats = new List<Discount_Category>();

                for (var i = 0; i < (item.DisItems?.Count ?? 0); i++)
                    if (item.DisItems[i].StartDateTime < App.CurrentDateTime &&
                        item.DisItems[i].EndDateTime > App.CurrentDateTime)
                        disItems.Add(item.DisItems[i]);

                for (var i = 0; i < (item.Cat.DisCats?.Count ?? 0); i++)
                    if (item.Cat.DisCats[i].StartDateTime < App.CurrentDateTime &&
                        item.Cat.DisCats[i].EndDateTime > App.CurrentDateTime)
                        disCats.Add(item.Cat.DisCats[i]);

                if (disItems.Count == 1)
                {
                    discountsUsed.Add(disItems.Last().Discount);
                }
                if (disCats.Count == 1)
                {
                    discountsUsed.Add(disCats.Last().Discount);
                }
                if (discountsUsed.Count == 0) continue;
                if (discountsUsed.Last().UsesPerTransaction != -1)
                    discountsUsed.Last().UsesPerTransaction -=
                        discountsUsed.Last().UsesPerTransaction <
                        (item.Amount / discountsUsed.Last().RequiredNumOfItems)
                            ? discountsUsed.Last().UsesPerTransaction
                            : (item.Amount / discountsUsed.Last().RequiredNumOfItems);

                discountAmount -= -discountsUsed.Last().Type == 0
                    ? (item.Amount / discountsUsed.Last().RequiredNumOfItems) * discountsUsed.Last().Amount
                    : (item.Amount / discountsUsed.Last().RequiredNumOfItems) * discountsUsed.Last().Amount *
                      item.Price;
            }

            total = Basket.Sum(item => item.Price * item.Amount) + discountAmount;

            for (var paid = 0.0m; paid < total;)
            {
                var action = await DisplayActionSheet(App.Translate.ProvideValue("PayMeth"), App.Translate.ProvideValue("Cancel"), null, App.Translate.ProvideValue("Card"), App.Translate.ProvideValue("Cash"));

                if (action == App.Translate.ProvideValue("Cancel"))
                {
                    App.DbContext.RevertDbContextChanges();
                    return;
                }

                var pay = new PaymentMethod_SaleModel() { PayMethod = actionDic[action]() };

                if (pay.PayMethod.MinimumCharge > total || refundOnly)
                {
                    total += pay.PayMethod.Charge;
                    var note = App.DbContext.GetNote(string.Format(App.Translate.ProvideValue("CardChargeNote"), pay.PayMethod.Charge));
                    if (note == null)
                    {
                        note = new NoteModel() { Note = string.Format(App.Translate.ProvideValue("CardChargeNote"), pay.PayMethod.Charge) };
                        App.DbContext.Add(note);
                    }
                    var notesSale = new Notes_SaleModel() { Note = note };
                    App.DbContext.Add(notesSale);
                    sale.Notes.Add(notesSale);
                }

                var amount = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                    string.Format(App.Translate.ProvideValue(refundOnly ? "HowMuchRefund" : "HowMuchPM"), action,
                        Math.Round(total - paid, 2, MidpointRounding.AwayFromZero)), "enter here", "Confrim",
                    App.Translate.ProvideValue("EnterCorrectValue"), total - paid, pay.PayMethod.IsCashBackable);

                pay.Amount = amount;

                paid += amount;

                if (paid > total)
                {
                    if (pay.PayMethod.IsChangeable)
                    {
                        change = pay.Change = paid - total;
                    }
                    else
                    {
                        paid -= amount;
                        continue;
                    }
                }
                App.DbContext.Add(pay);
                sale.PaySales.Add(pay);
            }
            sale.Total = total;

            if (sale.PaySales.Any(pay => pay.PayMethod.IsCashBackable))
            {
                var cashback = await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("CashBack_"), App.Translate.ProvideValue("Yes"), App.Translate.ProvideValue("Cancel"));
                if (cashback)
                {
                    var amount = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(App.Translate.ProvideValue("HowMuchCB"), "enter here", "Confrim", App.Translate.ProvideValue("EnterCorrectValue"), toPay:0.0m);
                    if (amount > 0.01m)
                    {
                        var cB = App.DbContext.GetNote(string.Format(App.Translate.ProvideValue("CBNote"), amount));
                        if(cB == null)
                        {
                            cB = new NoteModel { Note = string.Format(App.Translate.ProvideValue("CBNote"), amount) };
                            App.DbContext.Add(cB);
                        }
                        var cBNSale = new Notes_SaleModel { Note = cB };
                        App.DbContext.Add(cBNSale);
                        sale.Notes.Add(cBNSale);
                        change += amount;
                        total += amount;
                    }
                }
            }

            var Continue = await DisplayAlert(App.Translate.ProvideValue("Hmm"), String.Format(App.Translate.ProvideValue("Continue"), Math.Round(total, 2, MidpointRounding.AwayFromZero)), App.Translate.ProvideValue("Yes"), App.Translate.ProvideValue("Cancel"));
            if (!Continue)
            {
                App.DbContext.RevertDbContextChanges();
                return;
            }

            sale.Transactions = new List<TransactionModel>();
            sale.Refunds = new List<RefundModel>();

            foreach (var item in Basket)
            {
                var Item = new ItemModel(item);
                if (item.Return)
                {
                    var refund = new RefundModel() { ItemId = item.Id, Sale = sale, SaleIdReturned = item.SaleId, Reason = item.Reason, Amount = item.Amount };
                    sale.Refunds.Add(refund);
                    App.DbContext.Add(refund);
                }
                else
                {
                    var tran = new TransactionModel() { Item = Item, Sale = sale, Amount = item.Amount };
                    sale.Transactions.Add(tran);
                    App.DbContext.Add(tran);
                }
            }

            if (sale.Refunds.Count == 0)
                FinaliseTransaction(sale, change);
            else if(!Authorisation.IsAuthorised("Refund", "X", Basket.Where(item=>item.Return.Equals(true)).Sum(item=>item.Price*item.Amount), App.LastAuthUser))
            {
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("RefundLimitTooLow"), App.Translate.ProvideValue("OK"));
                Action action = () => FinaliseTransaction(sale, change);
                Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Refund", "X", Basket.Where(item => item.Return.Equals(true)).Sum(item => item.Price * item.Amount), action);
            }
            else
                FinaliseTransaction(sale, change);
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

            var itemHasNoStock = false;
            
            foreach (var trans in Sale.Transactions)
            {
                if (trans.Item.Stock != null) trans.Item.Stock.Quantity -= trans.Amount;
                else itemHasNoStock = true;
            }

            App.DbContext.Add(Sale);

            if (!App.DbContext.Save())
            {
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("DbIssue"), App.Translate.ProvideValue("OK"));
                return;
            }

            var pdf = new PDFCreator(App.Store, null, Sale, cashBack);
            
            Basket.Clear();
            await DisplayAlert(App.Translate.ProvideValue("Transaction"), App.Translate.ProvideValue("TransConfMesg"), App.Translate.ProvideValue("OK"));

            if (App.OneTimeStockWarning == false)
                if(itemHasNoStock == false)
                    return;

            await DisplayAlert(App.Translate.ProvideValue("Warning"), App.Translate.ProvideValue("MissingStock"),
                App.Translate.ProvideValue("OK"));
            App.OneTimeStockWarning = true;
        }

        private ItemModel FindItem(string needle)
        {
            return App.DbContext.SearchId(ManScan.Text)
                .Include(i => i.DisItems)
                    .ThenInclude(di=>di.Discount)
                .Include(i => i.Cat.DisCats)
                    .ThenInclude(dc => dc.Discount)
                .Include(i=>i.Stock)
                .SingleOrDefault();
        }

        /// <summary>
        /// On ManScan complete
        /// get item from DB if only one returns run AddBasket with item passed as Basket else throw error message
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private async void ManScan_Completed(object sender, EventArgs e)
        {
            var tempItem = FindItem(ManScan.Text);
            if (tempItem == null)
            {
                ManScan.Text = null;
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("ItemIdNotFoundMesg"), App.Translate.ProvideValue("OK"));
                return;
            }
            ManScan.Text = null;
            //var s = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
            //Debug.WriteLine(s);
            BasketAdd(new Basket(tempItem));
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
            var amount = (int)await AmountToAdd.Text.ToInterger(App.Translate.ProvideValue("ValueEnteredWrong"));
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
            StoredTrans.Add(_countBasketNum, new ObservableCollection<Basket>(Basket));
            Basket.Clear();
            CheckStoreTransExist();
            _countBasketNum++;
        }

        /// <summary>
        /// Check if StoredTrans has one item if it does run through ToolBarItems check if Baskets already exists if it does escape else create it
        /// if StoredTrans has zero items then remove Baskets from ToolBarItems
        /// </summary>
        private void CheckStoreTransExist()
        {
            if (StoredTrans.Count != 1)
            {
                if (StoredTrans.Count == 0)
                {
                    App.Current.MainPage.ToolbarItems.Clear();
                }
            }
            else
            {
                foreach (var item in App.Current.MainPage.ToolbarItems)
                {
                    if (item.Text == App.Translate.ProvideValue("Baskets"))
                        return;
                }

                App.Current.MainPage.ToolbarItems.Add(new ToolbarItem
                {
                    Text = App.Translate.ProvideValue("Baskets"),
                    Icon = "",
                    Command = new Command(this.ShowBasketList)
                });
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
            Basket item = null;
            foreach (var tempItem in Basket)
            {
                if (tempItem.Id == menuItem.Id && tempItem.Return == menuItem.Return && tempItem.SaleId == menuItem.SaleId)
                {
                    item = new Models.Basket(tempItem);
                }
            }
            await Navigation.PushAsync(new ReturnFormPage(item));
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
                    for (var i = 1; i <= StoredTrans.Last().Key; i++)
                    {
                        if (i <= selectedBasket.Key) continue;
                        var itemKey = i;
                        var itemData = StoredTrans[i];
                        itemKey = itemKey - 1;
                        StoredTrans.Remove(i);
                        StoredTrans.Add(itemKey, itemData);
                    }
                }
                page._countBasketNum--;
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

        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (ManScan.IsVisible)
                ManScan.SetFocusAfterDelay(1);
        }
    }
}