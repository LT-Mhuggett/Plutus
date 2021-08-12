using CommonPOSLibrary.Exceptions;
using Database.Enums;
using Database.Models;
using Microsoft.EntityFrameworkCore;
using NatApp.Plutus.Helpers.Extensions;
using NatApp.Plutus.Helpers.Security;
using NatApp.Plutus.Helpers.Validators;
using NatApp.Plutus.Models;
using NatApp.Plutus.Services.POSHandeling;
using Newtonsoft.Json;
using Plugin.Iconize;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace NatApp.Plutus.ViewModels.MainTill.Till
{
    public class TillViewModel : BaseViewModel
    {
        #region Private Fields
        private bool _isDesktop;
        private string _itemId;
        private int _quantity;
        private bool _pickerIsOpen;
        private IBasketRecord _selectedBasketRecord;
        #endregion

        #region Properties
        #region Public
        public bool IsDesktop
        {
            get => _isDesktop;
            set => SetProperty(ref _isDesktop, value);
        }
        public double TopBarFontSize => Device.GetNamedSize(NamedSize.Medium, typeof(Entry));
        public string ItemId
        {
            get => _itemId;
            set => SetProperty(ref _itemId, value);
        }
        public int Quantity
        {
            get => _quantity;
            set => SetProperty(ref _quantity, value);
        }
        public ObservableCollection<SavedTransactionModel> StoredTransactions { get; private set; } = new ObservableCollection<SavedTransactionModel>();
        public ObservableCollection<IBasketRecord> Basket { get; } = new ObservableCollection<IBasketRecord>();
        public IBasketRecord SelectedBasketRecord
        {
            get => _selectedBasketRecord;
            set => SetProperty(ref _selectedBasketRecord, value);
        }
        public decimal SaleExTax
        {
            get => Basket.Sum(bR => bR.PriceExTax * (bR is BasketReturnItem ? -1 : 1) * bR.Quantity);
        }
        public decimal SaleIncTax
        {
            get => Basket.Sum(bR => bR.Price * (bR is BasketReturnItem ? -1 : 1) * bR.Quantity);
        }
        public ObservableCollection<DiscountModel> Alterations { get; } = new ObservableCollection<DiscountModel>();
        public ObservableCollection<string> AlterationNames
        {
            get
            {
                var discountNames = new ObservableCollection<string>();
                foreach (var discount in Alterations)
                {
                    discountNames.Add(discount.Name);
                }
                return discountNames;
            }
        }
        public bool PickerOpen
        {
            get => _pickerIsOpen;
            set => SetProperty(ref _pickerIsOpen, value);
        }
        #endregion
        #endregion

        public TillViewModel()
        {
            #region Init
            Title = "Till".Translate();
            Icon = "md-store";

            #region Events
            StoredTransactions.CollectionChanged += (sender, e) =>
            {
                if (StoredTransactions.Count == 0)
                {
                    var toolbarItem = App.Current.MainPage.ToolbarItems.FirstOrDefault(tI => tI.Text.Equals("Baskets".Translate()));
                    if (toolbarItem != null)
                    {
                        App.Current.MainPage.ToolbarItems.Remove(toolbarItem);
                        App.GetViewModel().ToolbarItemsChanged = true;
                    }
                }
                else
                {
                    if (App.Current.MainPage.ToolbarItems.Any(tI => tI.Text.Equals("Baskets".Translate())))
                        return;
                    App.Current.MainPage.ToolbarItems.Insert(0, new IconToolbarItem
                    {
                        Text = "Baskets".Translate(),
                        IconImageSource = "md-shopping-basket",
                        IconColor = Color.White,
                        Command = RetrieveTransactionCommand,
                        IsVisible = true
                    });
                    App.GetViewModel().ToolbarItemsChanged = true;
                }
            };
            Basket.CollectionChanged += (sender, e) =>
            {
                if (e.NewItems != null)
                    foreach (INotifyPropertyChanged added in e.NewItems)
                        added.PropertyChanged += BasketRecordOnPropertyChanged;
                if (e.OldItems != null)
                    foreach (INotifyPropertyChanged removed in e.OldItems)
                        removed.PropertyChanged -= BasketRecordOnPropertyChanged;
                OnPropertyChanged("Basket");
                OnPropertyChanged("SaleExTax");
                OnPropertyChanged("SaleIncTax");
            };
            Alterations.CollectionChanged += (sender, e) =>
            {
                OnPropertyChanged("Alterations");
                OnPropertyChanged("AlterationNames");
            };
            #endregion
            Device.BeginInvokeOnMainThread(() =>
            {
                //Load SavedTranasactions
                Enum.TryParse(DatabaseProviderSetting, out DatabaseProvider databaseProvider);
                using (var db = new Helpers.Database.Database(databaseProvider))
                {
                    if (db.Get<SavedTransactionModel>().Any())
                    {
                        var savedTransactions = db.Get<SavedTransactionModel>();
                        foreach (var savedTransaction in savedTransactions)
                        {
                            StoredTransactions.Add(savedTransaction);
                        }
                    }
                }
            });

            MessagingCenter.Subscribe<Inventory.Items.ViewAllViewModel, string>(
            this,
            "AddToBasket",
            (sender, arg) =>
            {
                ExecuteItemAddArg(arg);
            });

            #endregion
            IsDesktop = Device.Idiom == TargetIdiom.Desktop;
            Quantity = 1;
        }

        #region Commands
        #region Items
        #region Adding
        #region Manual
        Command _manualAddCommand;

        public Command ManualAddCommand
        {
            get => _manualAddCommand ?? (_manualAddCommand = new Command(ExecuteItemAdd, () => !string.IsNullOrEmpty(ItemId)));
        }
        #endregion
        #region Manual with arg
        Command _manualAddCommandArg;

        public Command ManualAddCommandArg
        {
            get => _manualAddCommandArg ?? (_manualAddCommandArg = new Command<string>(ExecuteItemAddArg, (id) => !string.IsNullOrEmpty(id)));
        }
        #endregion
        #region AutoScan
        Command _autoScanCommand;

        public Command AutoScanCommand
        {
            get => _autoScanCommand ?? (_autoScanCommand = new Command(ExecuteAutoScan));
        }
        #endregion
        #endregion
        #region Removing
        Command _removeOneCommandArg;

        public Command RemoveOneCommandArg
        {
            get => _removeOneCommandArg ?? (_removeOneCommandArg = new Command<IBasketRecord>(ExecuteRemoveOne));
        }

        Command _removeAllCommandArg;

        public Command RemoveAllCommandArg
        {
            get => _removeAllCommandArg ?? (_removeAllCommandArg = new Command<IBasketRecord>(ExecuteRemoveAll));
        }
        #endregion
        #region Adjust
        Command _adjustCommandArg;

        public Command AdjustCommandArg
        {
            get => _adjustCommandArg ?? (_adjustCommandArg = new Command<BasketItem>(ExecuteAdjustItem));
        }
        #endregion
        #region Return
        Command _returnCommandArg;

        public Command ReturnCommandArg
        {
            get => _returnCommandArg ?? (_returnCommandArg = new Command<BasketItem>(ExecuteReturn));
        }

        Command _revertReturnCommandArg;

        public Command RevertReturnCommandArg
        {
            get => _revertReturnCommandArg ?? (_revertReturnCommandArg = new Command<BasketReturnItem>(ExecuteRevertReturn));
        }

        #endregion
        #endregion
        #region Transactions
        #region Alter
        Command _alterTransactionSelectorCommand;

        public Command AlterTransactionSelectorCommand
        {
            get => _alterTransactionSelectorCommand ?? (_alterTransactionSelectorCommand = new Command(ExecuteAlterTransactionSelector));
        }

        Command _alterTransactionCommand;

        public Command AlterTransactionCommand
        {
            get => _alterTransactionCommand ?? (_alterTransactionCommand = new Command<int>(ExecuteAlterTransaction));
        }
        #endregion
        #region Store
        Command _storeTransactionCommand;

        public Command StoreTransactionCommand
        {
            get => _storeTransactionCommand ?? (_storeTransactionCommand = new Command(ExecuteStoreTransaction));
        }
        #endregion
        #region Retrieve
        Command _retrieveTransactionCommand;

        public Command RetrieveTransactionCommand
        {
            get => _retrieveTransactionCommand ?? (_retrieveTransactionCommand = new Command(ExecuteRetrieveTransaction));
        }
        #endregion
        #region Checkout
        Command _checkoutTransactionCommand;

        public Command CheckoutTransactionCommand
        {
            get => _checkoutTransactionCommand ?? (_checkoutTransactionCommand = new Command(ExecuteCheckoutTransaction));
        }
        #endregion
        #region Cancel
        Command _cancelTransactionCommand;

        public Command CancelTransactionCommand
        {
            get => _cancelTransactionCommand ?? (_cancelTransactionCommand = new Command(ExecuteCancelTransaction));
        }
        #endregion
        #endregion

        #endregion

        #region Command Execution
        #region Items
        #region Add
        private async void ExecuteItemAdd()
        {
            if (IsBusy)
                return;
            IsBusy = true;

            try
            {
                var item = FindItem(ItemId);
                if (item == null)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "ItemNotFoundMesg".Translate(), "OK".Translate());
                    return;
                }

                BasketItem tempItem = default;

                if (SelectedBasketRecord != null &&
                    (SelectedBasketRecord is BasketItem) &&
                    (SelectedBasketRecord as BasketItem).Item.Id.Equals(item.Id) &&
                    !(SelectedBasketRecord is BasketReturnItem))
                {
                    tempItem = SelectedBasketRecord as BasketItem;
                }
                else
                {
                    tempItem = Basket.Where(bR => bR is BasketItem)
                        .Cast<BasketItem>()
                        .LastOrDefault(bI => bI.Item.Id.Equals(item.Id) &&
                            bI.Price.Equals(item.Price) &&
                            bI.PriceExTax.Equals(item.ExPrice) &&
                            !(bI is BasketReturnItem));
                }

                if (tempItem == default)
                    Basket.Add(new BasketItem(item, Quantity));
                else
                    tempItem.IncrementQuantity(Quantity);
                ItemId = string.Empty;
                Quantity = 1;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void ExecuteItemAddArg(string id)
        {
            if (IsBusy)
                return;
            IsBusy = true;

            try
            {
                var item = FindItem(id);
                if (item == null)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "ItemNotFoundMesg".Translate(), "OK".Translate());
                    return;
                }

                BasketItem tempItem = default;

                if (SelectedBasketRecord != null &&
                    (SelectedBasketRecord is BasketItem) &&
                    (SelectedBasketRecord as BasketItem).Item.Id.Equals(item.Id) &&
                    !(SelectedBasketRecord is BasketReturnItem))
                {
                    tempItem = SelectedBasketRecord as BasketItem;
                }
                else
                {
                    tempItem = Basket.Where(bR => bR is BasketItem)
                        .Cast<BasketItem>()
                        .LastOrDefault(bI => bI.Item.Id.Equals(item.Id) &&
                            bI.Price.Equals(item.Price) &&
                            bI.PriceExTax.Equals(item.ExPrice) &&
                            !(bI is BasketReturnItem));
                }

                if (tempItem == default)
                    Basket.Add(new BasketItem(item, Quantity));
                else
                    tempItem.IncrementQuantity(Quantity);
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion
        #region Remove
        private void ExecuteRemoveOne(IBasketRecord basketRecord)
        {
            if (IsBusy)
                return;
            IsBusy = true;
            try
            {
                if (basketRecord.Quantity > 1)
                    basketRecord.Quantity--;
                else
                    Basket.Remove(basketRecord);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ExecuteRemoveAll(IBasketRecord basketRecord)
        {
            if (IsBusy)
                return;
            IsBusy = true;
            try
            {
                Basket.Remove(basketRecord);
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion
        #region Adjust
        private async void ExecuteAdjustItem(BasketItem basketItem)
        {
            if (IsBusy)
                return;
            IsBusy = true;
            try
            {
                var numstyle = NumberStyles.AllowCurrencySymbol | NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign;
                var validators = new IValidator[]
                {
                    new RequiredValidator(),
                    new CurrencyValueValidator(numstyle)
                };
                var elements = new Tuple<string, string, IEnumerable<IValidator>, bool, bool>[]
                {
                    Tuple.Create("PriceExTax".Translate(), basketItem.PriceExTax.ToString("C", CultureInfo.CurrentCulture), validators.AsEnumerable(), false, true),
                    Tuple.Create("Price".Translate(), basketItem.Price.ToString("C", CultureInfo.CurrentCulture), validators.AsEnumerable(), false, true)
                };
                var data = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(elements, "Confirm".Translate(), true, "Adjust".Translate());
                basketItem.PriceExTax = decimal.Parse((string)data.ElementAt(0), numstyle, CultureInfo.CurrentCulture);
                basketItem.Price = decimal.Parse((string)data.ElementAt(1), numstyle, CultureInfo.CurrentCulture);
                Microsoft.AppCenter.Analytics.Analytics.TrackEvent("Item Adjustment");
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion
        #region Return
        private async void ExecuteReturn(BasketItem basketItem)
        {
            if (IsBusy)
                return;
            IsBusy = true;
            try
            {
                #region Setup Input Alert
                var stringValidators = new List<IValidator>
                {
                    new RequiredValidator()
                };

                var elements = new Tuple<string, string, IEnumerable<IValidator>, bool, bool>[2]
                {
                    Tuple.Create(string.Format("IdArg".Translate(), "Sale".Translate()), "", stringValidators.AsEnumerable(), false, true),
                    Tuple.Create("Reason".Translate(), "ReturnReasonExample".Translate(), stringValidators.AsEnumerable(), false, true)
                };
                #endregion

                var returnItem = App.GetViewModel().GetMapper.Map<BasketReturnItem>(basketItem);

                var _continueLoop = false;
                do
                {
                    var data = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(elements, "Confirm".Translate(), true, "Returns".Translate(), "Cancel".Translate());
                    if (data[0].ToString() == default && data[1].ToString() == default)
                        return;

                    if (string.IsNullOrEmpty(data[0].ToString()) || string.IsNullOrEmpty(data[1].ToString()))
                    {
                        _continueLoop = true;
                        continue;
                    }

                    Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
                    using (var db = new Helpers.Database.Database(databaseProvider))
                    {
                        if (!db.IsExists<SaleModel, string>(data[0].ToString()))
                        {
                            _continueLoop = true;
                            await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "SaleIDWrongMesg".Translate(), "OK".Translate());
                            continue;
                        }

                        var trans = db.Get<TransactionModel>()
                            .Include(t => t.Sale)
                                .ThenInclude(s => s.Refunded)
                            .Include(t => t.CheckoutItemChange)
                            .Where(t => t.SaleId.Equals(data[0].ToString()) && t.ItemId.Equals(basketItem.Item.Id));
                        TransactionModel tran = null;

                        if (trans.Count() > 1)
                        {
                            foreach (var tempTran in trans)
                            {
                                if (tempTran.CheckoutItemChange != null && tempTran.CheckoutItemChange.Price == basketItem.Price ||
                                    tempTran.ItemCostPrice == basketItem.Price && tempTran.ItemCostExPrice == basketItem.PriceExTax)
                                {
                                    tran = tempTran;
                                    break;
                                }
                            }
                        }
                        else
                            tran = trans.Last();
                        if (tran == null)
                        {
                            _continueLoop = true;
                            await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "ItemNotExistInSaleMesg".Translate(), "OK".Translate());
                            continue;
                        }
                        else
                        {
                            tran = trans.Last();
                            if (tran.Amount < basketItem.Quantity)
                            {
                                await App.Current.MainPage.DisplayAlert("Hmm".Translate(), string.Format("ItemAmountExceedsMesg".Translate(), basketItem.Name, tran.Amount, basketItem.Quantity - tran.Amount), "OK".Translate());
                                return;
                            }
                            else
                            {
                                int refundsLeft = tran.Amount;
                                if (tran.Sale.Refunded != null)
                                {
                                    refundsLeft -= tran.Sale.Refunded.Where(r => r.ItemId.Equals(basketItem.Item.Id)).Sum(r => r.Amount);
                                }

                                if (refundsLeft < basketItem.Quantity)
                                {
                                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(), string.Format("NoRefundsLeftMesg".Translate(), refundsLeft, basketItem.Name), "OK".Translate());
                                    return;
                                }

                                returnItem.PriceExTax = tran.CheckoutItemChange == null ? tran.ItemCostExPrice : tran.CheckoutItemChange.ExPrice;
                                returnItem.Price = tran.CheckoutItemChange == null ? tran.ItemCostPrice : tran.CheckoutItemChange.Price;
                                returnItem.SetItemReturn(data[1].ToString(), data[0].ToString());
                            }
                        }
                    }
                } while (_continueLoop);

                //finalize change
                Basket.Remove(basketItem);
                Basket.Add(returnItem);
                Microsoft.AppCenter.Analytics.Analytics.TrackEvent("Item Return");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ExecuteRevertReturn(BasketReturnItem basketReturnItem)
        {
            if (IsBusy)
                return;
            IsBusy = true;
            try
            {
                var basketItem = App.GetViewModel().GetMapper.Map<BasketItem>(basketReturnItem);

                //finalize change
                Basket.Remove(basketReturnItem);
                Basket.Add(basketItem);
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion
        #endregion
        private void ExecuteAutoScan()
        {
            throw new NotImplementedException();
        }

        #region Transaction
        #region Alter
        private void ExecuteAlterTransactionSelector()
        {
            if (IsBusy)
                return;
            IsBusy = true;

            try
            {
                Alterations.Clear();

                Microsoft.AppCenter.Analytics.Analytics.TrackEvent("Transaction Alteration (Discounts)");
                Enum.TryParse(DatabaseProviderSetting, out DatabaseProvider databaseProvider);
                using (var db = new Helpers.Database.Database(databaseProvider, App.GetViewModel().EmployeeId))
                {
                    foreach (var discount in db.Get<DiscountModel>())
                        Alterations.Add(discount);
                }
                PickerOpen = true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void ExecuteAlterTransaction(int selectedAlteration)
        {
            if (IsBusy)
                return;
            IsBusy = true;

            try
            {
                var alteration = Alterations.ElementAt(selectedAlteration);
                var items = new List<BasketItem>();

                foreach (var item in Basket.Where(br => br is BasketItem).Cast<BasketItem>().ToList())
                {
                    for (int i = 0; i < item.Quantity; i++)
                        items.Add((BasketItem)item.Clone());
                }

                var entries = new List<Tuple<string, string, IEnumerable<IValidator>, bool, bool>>();

                if (alteration.Amount == 0.0m)
                    entries.Add(new Tuple<string, string, IEnumerable<IValidator>, bool, bool>(
                        alteration.Type == 0 ? "Cash".Translate() : "Percent".Translate(), "0", new List<IValidator>(), false, true));

                else
                    entries.Add(new Tuple<string, string, IEnumerable<IValidator>, bool, bool>(
                        alteration.Type == 0 ? "Cash".Translate() : "Percent".Translate(), alteration.Amount.ToString(), new List<IValidator>(), false, false));

                //data type -> Tuple<List<string>, List<BasketItem>>
                var data = await Helpers.CustomViews.InputMultiSelectAlertHelper<string, BasketItem>.LaunchInputMulitSelectAlertAsync(entries, Tuple.Create<IEnumerable<BasketItem>, string>(items, "Name"), default, "Confirm".Translate(), false, "Alterations".Translate());

                BasketAlteration adjustment;

                if (data.Item2.Count() < items.Count())
                {
                    foreach (var item in data.Item2)
                    {
                        if (alteration.Type == 0)
                        {
                            var alterationAmount = Math.Abs(Math.Round(Decimal.Parse(data.Item1.First()), 2, MidpointRounding.AwayFromZero)) * -1;
                            adjustment = new BasketAlteration(new NoteModel($"{alteration.Name}, {item.Name} {alterationAmount.ToString("C2", CultureInfo.CurrentCulture)}"), alteration, item, alterationAmount, alterationAmount);

                        }
                        else
                        {
                            var alterationAmount = Tuple.Create(Math.Abs(Math.Round(item.Price * Decimal.Parse(data.Item1.First()), 2, MidpointRounding.AwayFromZero)) * -1, Math.Abs(Math.Round(item.PriceExTax * Decimal.Parse(data.Item1.First()), 2, MidpointRounding.AwayFromZero)) * -1);
                            adjustment = new BasketAlteration(new NoteModel($"{alteration.Name}, {item.Name} {alterationAmount.Item1.ToString("C2", CultureInfo.CurrentCulture)}"), alteration, item, alterationAmount.Item1, alterationAmount.Item2);
                        }
                        Basket.Add(adjustment);
                    }
                }
                else
                {
                    if (alteration.Type == 0)
                    {
                        var alterationAmount = Math.Abs(Math.Round(Decimal.Parse(data.Item1.First()) * data.Item2.Count(), 2, MidpointRounding.AwayFromZero)) * -1;
                        adjustment = new BasketAlteration(new NoteModel($"{alteration.Name}, {alterationAmount.ToString("C2", CultureInfo.CurrentCulture)}"), alteration, data.Item2, alterationAmount, alterationAmount);
                    }
                    else
                    {
                        var alterationAmount = Tuple.Create(Math.Abs(Math.Round(data.Item2.Sum(tempItem => tempItem.Price) * Decimal.Parse(data.Item1.First()), 2, MidpointRounding.AwayFromZero)) * -1, Math.Abs(Math.Round(data.Item2.Sum(tempItem => tempItem.PriceExTax) * Decimal.Parse(data.Item1.First()), 2, MidpointRounding.AwayFromZero)) * -1);
                        adjustment = new BasketAlteration(new NoteModel($"{alteration.Name}, {alterationAmount.Item1.ToString("C2", CultureInfo.CurrentCulture)}"), alteration, data.Item2, alterationAmount.Item1, alterationAmount.Item2);
                    }
                    Basket.Add(adjustment);
                }

            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion

        #region Stored Transactions
        private async void ExecuteStoreTransaction()
        {
            if (IsBusy == true || Basket.Count == 0)
                return;
            IsBusy = true;
            try
            {
                var validators = new IValidator[]
                {
                    new RequiredValidator()
                };
                var elements = new Tuple<string, string, IEnumerable<IValidator>, bool, bool>[]
                {
                    Tuple.Create("Name".Translate(), "", validators.AsEnumerable(), false, true)
                };

                string data;
                bool firstRun = true;
                do
                {
                    if (!firstRun)
                        await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "NameMustBeUniqueMesg".Translate(), "OK".Translate());

                    data = (string)(await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(elements, "Confirm".Translate(), false, "TransactionName".Translate(), "Cancel".Translate())).First();

                    if (data == default)
                    {

                        Microsoft.AppCenter.Analytics.Analytics.TrackEvent("Transaction Store (Saving)", new Dictionary<string, string> { { "Canceled", "True" } });
                        return;
                    }
                    firstRun = false;
                } while (StoredTransactions.Any(sT => sT.Name.Equals(data)));


                var basketRecords = new IBasketRecord[Basket.Count];

                Basket.CopyTo(basketRecords, 0);

                StoredTransactions.Add(
                    new SavedTransactionModel
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = data,
                        Data = JsonConvert.SerializeObject(
                            basketRecords,
                            Formatting.Indented,
                            new JsonSerializerSettings
                            {
                                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                                TypeNameHandling = TypeNameHandling.Auto
                            })
                    });
                Enum.TryParse(DatabaseProviderSetting, out DatabaseProvider databaseProvider);
                using (var db = new Helpers.Database.Database(databaseProvider))
                {
                    db.Add(StoredTransactions.Last());
                    if (!db.Save())
                    {
                        await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "CriticalIssue".Translate(), "OK".Translate());
                        StoredTransactions.RemoveAt(StoredTransactions.Count());
                        return;
                    }
                }
                Basket.Clear();

                Microsoft.AppCenter.Analytics.Analytics.TrackEvent("Transaction Store (Saving)", new Dictionary<string, string> { { "Canceled", "False" } });
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void ExecuteRetrieveTransaction()
        {
            if (IsBusy)
                return;
            IsBusy = true;
            try
            {
                SavedTransactionModel storedTransaction;
                if (StoredTransactions.Count > 1)
                {
                    var baskets = new string[StoredTransactions.Count];
                    for (int i = 0; i < StoredTransactions.Count; i++)
                        baskets[i] = StoredTransactions.ElementAt(i).Name;
                    var action = await App.Current.MainPage.DisplayActionSheet("Baskets".Translate(), "Cancel".Translate(), null, baskets);
                    if (action == "Cancel".Translate())
                    {
                        Microsoft.AppCenter.Analytics.Analytics.TrackEvent("Transaction Retreived", new Dictionary<string, string> { { "Canceled", "True" } });
                        return;
                    }
                    storedTransaction = StoredTransactions.First(sT => sT.Name.Equals(action));
                }
                else
                {
                    storedTransaction = StoredTransactions.First();
                }

                if (Basket.Count > 0)
                    if (!await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "BasketWillBeClearedMesg".Translate(), "OK".Translate(), "Cancel".Translate()))
                        return;

                Enum.TryParse(DatabaseProviderSetting, out DatabaseProvider databaseProvider);
                using (var db = new Helpers.Database.Database(databaseProvider))
                {
                    db.Delete(new SavedTransactionModel { Id = storedTransaction.Id });
                    if (!db.Save())
                    {
                        await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "CriticalIssue".Translate(), "OK".Translate());
                        return;
                    }
                }
                StoredTransactions.Remove(storedTransaction);
                var storedTransactionData = storedTransaction.Data;
                var basket = JsonConvert.DeserializeObject<IBasketRecord[]>(
                    storedTransactionData, new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Auto
                    });

                Basket.Clear();
                foreach (var item in basket)
                    Basket.Add(item);

                Microsoft.AppCenter.Analytics.Analytics.TrackEvent("Transaction Store (Saving)", new Dictionary<string, string> { { "Canceled", "False" } });
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion

        private async void ExecuteCheckoutTransaction()
        {

            if (IsBusy == true)
                return;
            IsBusy = true;
            try
            {
                string empId = App.GetViewModel().EmployeeId;

                var sale = new SaleModel
                {
                    DateOfSale = DateTime.Now,
                    Total = 0.0m,
                    EmployeeId = empId,
                    PaySales = new List<PaymentMethod_SaleModel>(),
                    Notes = new List<Notes_SaleModel>()
                };

                var change = 0.0m;

                var refundOnly = !Basket.Any(bR => bR is BasketItem && !(bR is BasketReturnItem));

                var payMeths = GenPaymentMethodActions();
                Enum.TryParse(DatabaseProviderSetting, out DatabaseProvider databaseProvider);

                sale.Total = Basket.Sum(bR => bR.Price * (bR is BasketReturnItem ? -1 : 1) * bR.Quantity);
                sale.TotalExTax = Basket.Sum(bR => bR.PriceExTax * (bR is BasketReturnItem ? -1 : 1) * bR.Quantity);

                for (var paid = 0.0m; paid != sale.Total;)
                {
                    if (!Basket.Any(br => br is BasketReturnItem))
                        if (paid > sale.Total)
                            break;
                    var payMethNames = payMeths.Keys.ToArray();

                    var payMeth = await App.Current.MainPage.DisplayActionSheet("PayMeth".Translate(), "Cancel".Translate(), null, payMethNames);

                    if (payMeth == "Cancel".Translate())
                    {
                        Microsoft.AppCenter.Analytics.Analytics.TrackEvent("Sale Processing", new Dictionary<string, string> { { "Canceled", "True" } });
                        return;
                    }

                    var pay = new PaymentMethod_SaleModel() { TempPayMethod = payMeths[payMeth]() };

                    if (pay.TempPayMethod.MinimumCharge > sale.Total || !refundOnly)
                    {
                        if (pay.TempPayMethod.Charge != 0.0m)
                        {
                            using (var db = new Helpers.Database.Database(databaseProvider))
                            {
                                var note = db.GetNote(string.Format("CardChangeNote".Translate(), pay.TempPayMethod.Charge));
                                if (note == null)
                                    note = new NoteModel(string.Format("CardChangeNote".Translate(), pay.TempPayMethod.Charge));

                                Basket.Add(new BasketNote(note, pay.TempPayMethod.Charge, pay.TempPayMethod.Charge));
                                sale.Total = Basket.Sum(bR => bR.Price * (bR is BasketReturnItem ? -1 : 1) * bR.Quantity);
                            }
                        }
                    }
                    #region Setup and run payment amount input
                    var numstyle = NumberStyles.AllowCurrencySymbol | NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign;
                    var validators = new IValidator[]
                        {
                            new RequiredValidator(),
                            new CurrencyValueValidator(numstyle)
                        };
                    var elements = new Tuple<string, string, IEnumerable<IValidator>, bool, bool>[1]
                    {
                            Tuple.Create("Amount", "", validators.AsEnumerable(), false, true)
                    };

                    var datum = (await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                        elements,
                        "Confirm".Translate(),
                        false,
                        !pay.TempPayMethod.IsCashBackable || (pay.TempPayMethod.IsChangeable),
                        sale.Total - paid,
                        string.Format(
                            refundOnly ? "HowMuchRefund".Translate() : "HowMuchPM".Translate(),
                            payMeth,
                            Math.Round(sale.Total - paid, 2, MidpointRounding.AwayFromZero)))).First();

                    var amount = decimal.Parse((string)datum, numstyle, CultureInfo.CurrentCulture);
                    #endregion
                    pay.Amount = amount;
                    paid += amount;
                    if (paid > sale.Total)
                    {
                        if (pay.TempPayMethod.IsChangeable)
                        {
                            change = pay.Change = paid - sale.Total;
                        }
                        else
                        {
                            paid -= amount;
                            continue;
                        }
                    }
                    sale.PaySales.Add(pay);
                }

                if (sale.PaySales.Any(p => p.TempPayMethod.IsCashBackable) && CashbackEnabled)
                {
                    //Cashback stuff here
                }

                foreach (var basketNote in Basket.Where(bR => bR is BasketNote || bR is BasketAlteration).Cast<BasketNote>().ToList())
                {
                    var sNote = new Notes_SaleModel() { Note = basketNote.Note };
                    sale.Notes.Add(sNote);
                }

                var @continue = await App.Current.MainPage.DisplayAlert(
                    "Hmm".Translate(),
                    string.Format(
                        "TransacContinue".Translate(),
                        Math.Round(sale.Total,
                            2,
                            MidpointRounding.AwayFromZero).ToString("C", CultureInfo.CurrentCulture)),
                    "Yes".Translate(),
                    "Cancel".Translate());

                if (!@continue)
                    return;

                //Prepare sale for Transaction and Refunds adding
                sale.Transactions = new List<TransactionModel>();
                sale.Refunds = new List<RefundModel>();

                //Loop through all BasketItems in Basket
                foreach (var item in Basket.Where(bR => bR is BasketItem).Cast<BasketItem>().ToList())
                {
                    var tran = new TransactionModel() { ItemId = item.Item.Id, Sale = sale, Amount = item.Quantity, ItemCostExPrice = item.Item.ExPrice, ItemCostPrice = item.Item.Price, Transaction_Discounts = new ObservableCollection<TransactionModel_DiscountModel>() };
                    if (Basket.Where(bR => bR is BasketAlteration).Cast<BasketAlteration>().Any())
                    {
                        var tempIA = Basket.Where(bR => bR is BasketAlteration && !(bR is BasketReturnItem)).Cast<BasketAlteration>().Where(bA => bA.ItemsAssocitated.Any(iA => iA.Item.Id.Equals(item.Item.Id))).FirstOrDefault();
                        if (tempIA != default)
                        {
                            var tranDisc = new TransactionModel_DiscountModel { DiscountId = tempIA.Discount.Id };
                            tran.Transaction_Discounts.Add(tranDisc);
                        }
                    }
                    sale.Transactions.Add(tran);
                    if (item.PriceExTax != item.Item.ExPrice || item.Price != item.Item.Price)
                    {
                        var itemPriceChange = new CheckoutItemChangeModel() { ItemId = item.Item.Id, ExPrice = item.PriceExTax, Price = item.Price, Tran = tran };
                        tran.CheckoutItemChange = itemPriceChange;
                    }
                }

                foreach (var returnItem in Basket.Where(bR => bR is BasketReturnItem).Cast<BasketReturnItem>().ToList())
                {
                    var refund = new RefundModel() { ItemId = returnItem.Item.Id, Sale = sale, SaleIdReturned = returnItem.ReturnSaleId, Reason = returnItem.Reason, Amount = returnItem.Quantity };
                    sale.Refunds.Add(refund);
                    if (returnItem.PriceExTax != returnItem.Item.ExPrice || returnItem.Price != returnItem.Item.Price)
                    {
                        var itemPriceChange = new CheckoutItemChangeModel() { ItemId = returnItem.Item.Id, ExPrice = returnItem.PriceExTax, Price = returnItem.Price, Refund = refund };
                        refund.CheckoutItemChange = itemPriceChange;
                    }
                }

                bool escape = false;

                do
                {
                    if (empId.IsAuthorised("Till", Permissions.Execute, databaseProvider))
                    {
                        if (!sale.Refunds.Any())
                        {
                            FinaliseTransation(sale, change);
                            return;
                        }
                        var refundAmount = Basket.Where(bR => bR is BasketReturnItem).Sum(bRI => bRI.Price);
                        if (refundAmount <= 20m)
                        {
                            if (empId.IsAuthorised("Refund20", Permissions.Execute, databaseProvider))
                            {
                                FinaliseTransation(sale, change);
                                return;
                            }
                        }
                        else if (refundAmount <= 100)
                        {
                            if (empId.IsAuthorised("Refund100", Permissions.Execute, databaseProvider))
                            {
                                FinaliseTransation(sale, change);
                                return;
                            }
                        }
                        else
                        {
                            if (empId.IsAuthorised("Refund Unlimited", Permissions.Execute, databaseProvider))
                            {
                                FinaliseTransation(sale, change);
                                return;
                            }
                        }
                    }

                    var empAuthoriser = await Authorisation.RequestAuthorisedUserInput(databaseProvider);
                    if (empAuthoriser == default)
                        escape = true;

                } while (!escape);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ExecuteCancelTransaction()
        {
            Basket.Clear();
        }
        #endregion
        #endregion

        #region Operations
        private async void FinaliseTransation(SaleModel sale, decimal change)
        {
            _ = Enum.TryParse(DatabaseProviderSetting, out DatabaseProvider databaseProvider);
            using (var db = new Helpers.Database.Database(databaseProvider, App.GetViewModel().EmployeeId))
            {
                var itemHasNoStock = false;

                foreach (var tran in sale.Transactions)
                {
                    if (Basket.Where(bR => bR is BasketItem).Cast<BasketItem>().First(i => i.Item.Id.Equals(tran.ItemId)).Item.Stock != null)
                    {
                        var stock = db.Get<StockModel>().Where(s => s.ItemId.Equals(tran.ItemId) && s.StoreId.Equals(App.GetViewModel().Store.Id)).FirstOrDefault();
                        if (stock != default)
                        {
                            stock.Quantity -= tran.Amount;
                            db.Save();
                        }
                    }
                    else
                        itemHasNoStock = true;
                }

                sale.PaySales.ForEach(p => p.PayId = p.TempPayMethod.Id);

                db.Add(sale);

                if (!db.Save())
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "DbIssue".Translate(), "OK".Translate());
                    return;
                }

                Task[] tasks = new Task[3];

                var printerMgr = new PosPrinterManager();

                var trackEventArgs = new Dictionary<string, string>();
                trackEventArgs.Add("Canceled", "False");

                if (Device.Idiom == TargetIdiom.Desktop)
                {
                    if (!AskForReceipt || await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "ReceiptRequired".Translate(), "Yes".Translate(), "No".Translate()))
                    {
                        trackEventArgs.Add("Receipt Requested", "True");
                        tasks[0] = Task.Run(async () =>
                        {
                            _ = await printerMgr.InitPrinter();
                            await printerMgr.SetUpSalePrint(sale, Basket, App.GetViewModel().Store);
                            await printerMgr.ExecuteOposOrPdfAsync();
                            trackEventArgs.Add("Receipt Printed Succesfully", "True");
                        });
                    }

                    if (TryCashDrawer)
                    {
                        trackEventArgs.Add("Cash Drawer Open Requested", "True");
                        if (sale.PaySales.Any(pay => pay.TempPayMethod.IsChangeable.Equals(true)))
                        {
                            tasks[1] = printerMgr.OpenCashDrawer();
                            trackEventArgs.Add("Cash Drawer Opened Successfully", "True");
                        }
                    }
                }

                if (change != default)
                {
                    tasks[2] = App.Current.MainPage.DisplayAlert("Hmm".Translate(), string.Format("CashBack".Translate(), change), "OK".Translate());
                }

                try
                {
                    await Task.WhenAll(tasks.Where(t => t != null));
                    _ = await printerMgr.CloseConnection();
                    printerMgr.Dispose();
                }
                catch (POSObjectException pOSObjectException)
                {
                    Microsoft.AppCenter.Crashes.Crashes.TrackError(pOSObjectException);
                    if (pOSObjectException.POSTargetObjectType == CommonPOSLibrary.Enums.POSTargetObjectType.Printer)
                    {
                        trackEventArgs.Add("Receipt Printed Succesfully", "False");
                        await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "There was a problem with the POS Printer. Transaction has succeeded but a receipt is currently unavailble.", "OK".Translate());
                    }
                    else if (pOSObjectException.POSTargetObjectType == CommonPOSLibrary.Enums.POSTargetObjectType.CashDrawer)
                    {
                        trackEventArgs.Add("Cash Drawer Opened Successfully", "False");
                        CashDrawerWarningSilenced = !await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "CashDrawerErrorWarning".Translate(), "OK".Translate(), "Silence".Translate());
                    }
                }
                Basket.Clear();
                await App.Current.MainPage.DisplayAlert("Transaction".Translate(), "TransConfMesg".Translate(), "OK".Translate());

                Microsoft.AppCenter.Analytics.Analytics.TrackEvent("Sale Processing", trackEventArgs);

                if (!itemHasNoStock)
                {
                    return;
                }
                //put in stockwarning
            }
        }

        private Dictionary<string, Func<PaymentMethodModel>> GenPaymentMethodActions()
        {
            _ = Enum.TryParse(DatabaseProviderSetting, out DatabaseProvider databaseProvider);
            using (var db = new Helpers.Database.Database(databaseProvider))
            {
                return db.Get<PaymentMethodModel>()
                    .OrderBy(p => p.Name)
                    .ToDictionary<PaymentMethodModel, string, Func<PaymentMethodModel>>(payMeth => payMeth.Name, payMeth => () => payMeth);
            }
        }

        private ItemModel FindItem(string needle = "")
        {
            try
            {
                Enum.TryParse(DatabaseProviderSetting, out DatabaseProvider databaseProvider);
                using (var db = new Helpers.Database.Database(databaseProvider))
                {
                    return db.SearchId(needle)
                        .Include(i => i.DisItems)
                            .ThenInclude(di => di.Discount)
                        .Include(i => i.Cat)
                            .ThenInclude(c => c.DisCats)
                                .ThenInclude(dc => dc.Discount)
                        .Include(i => i.Stock)
                        .Include(i => i.Vat)
                        .AsNoTracking()
                        .SingleOrDefault();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                return null;
            }
        }
        #endregion

        #region INotifyPropertyChanged
        private void BasketRecordOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged("Basket");
            OnPropertyChanged("SaleExTax");
            OnPropertyChanged("SaleIncTax");
        }
        #endregion
    }
}