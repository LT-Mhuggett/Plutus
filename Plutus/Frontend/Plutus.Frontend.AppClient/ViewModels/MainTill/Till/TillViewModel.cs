using CommonPOSLibrary.Exceptions;
using Mapster;
using Microsoft.Maui.Devices;
using Microsoft.Maui.ApplicationModel;
using Plugin.Maui.MessagingCenter;
using CustomViews.Structs;
using Database.Enums;
using Database.Models;
using Microsoft.EntityFrameworkCore;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Helpers.Security;
using Plutus.Frontend.AppClient.Helpers.Validators;
using Plutus.Frontend.AppClient.Models;
using Plutus.Frontend.AppClient.Services.POSHandeling;
using Newtonsoft.Json;
using Plutus.Frontend.AppClient.Helpers.Compatibility;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Plutus.Frontend.AppClient.Services.Analytics;
using Plutus.SharedKernel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.ViewModels.MainTill.Till
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
        public double TopBarFontSize => new Label().FontSize;
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
        public ObservableCollection<SavedTransactionModel> StoredTransactions { get; } = new ObservableCollection<SavedTransactionModel>();
        public ObservableCollection<IBasketRecord> Basket { get; } = new ObservableCollection<IBasketRecord>();
        public IBasketRecord SelectedBasketRecord
        {
            get => _selectedBasketRecord;
            set => SetProperty(ref _selectedBasketRecord, value);
        }
        public decimal SaleExTax => Basket.Sum(bR => bR.PriceExTax * (bR is BasketReturnItem ? -1 : 1) * bR.Quantity);

        public decimal SaleIncTax => Basket.Sum(bR => bR.Price * (bR is BasketReturnItem ? -1 : 1) * bR.Quantity);
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

            // ⚠ THE BASKET OWNS THE CATALOGUE SYNC. A sync mid-basket rewrites prices under the
            // operator's hands: a line added before the tick and one added after would come from
            // different price lists, in one sale, and the receipt would be the only evidence it
            // happened. The heartbeat and the outbox drain are NOT held — neither touches the
            // catalogue, and a queued sale should not wait for a customer to finish paying.
            Services.Sync.TillCadence.BasketIsOpen = () => Basket.Count > 0;

            #region Events
            StoredTransactions.CollectionChanged += (sender, e) =>
            {
                if (StoredTransactions.Count == 0)
                {
                    var toolbarItem = Shell.Current.CurrentPage.ToolbarItems.FirstOrDefault(tI => tI.Text.Equals("Baskets".Translate()));
                    if (toolbarItem == null) return;

                    Shell.Current.CurrentPage.ToolbarItems.Remove(toolbarItem);
                    App.GetViewModel().ToolbarItemsChanged = true;
                }
                else
                {
                    if (Shell.Current.CurrentPage.ToolbarItems.Any(tI => tI.Text.Equals("Baskets".Translate()))) return;

                    Shell.Current.CurrentPage.ToolbarItems.Insert(0, new IconToolbarItem
                    {
                        Text = "Baskets".Translate(),
                        IconImageSource = "md-shopping-basket",
                        IconColor = Colors.White,
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
                OnPropertyChanged(nameof(Basket));
                OnPropertyChanged(nameof(SaleExTax));
                OnPropertyChanged(nameof(SaleIncTax));
            };
            Alterations.CollectionChanged += (sender, e) =>
            {
                OnPropertyChanged(nameof(Alterations));
                OnPropertyChanged(nameof(AlterationNames));
            };
            #endregion
            MainThread.BeginInvokeOnMainThread(() =>
            {
                //Load SavedTransactions
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

            MessagingCenter.Subscribe<Inventory.Items.ViewAllViewModel, string>(this, "AddToBasket", (sender, arg) =>
            {
                ExecuteItemAddArg(arg);
            });

            #endregion
            IsDesktop = DeviceInfo.Idiom == DeviceIdiom.Desktop;
            Quantity = 1;
        }

        #region Commands
        #region Items
        #region Adding
        #region Manual
        private Command _manualAddCommand;

        public Command ManualAddCommand => _manualAddCommand ?? (_manualAddCommand = new Command(ExecuteItemAdd, () => !string.IsNullOrEmpty(ItemId)));

        #endregion
        #region Manual with arg

        private Command _manualAddCommandArg;

        public Command ManualAddCommandArg => _manualAddCommandArg ?? (_manualAddCommandArg = new Command<string>(ExecuteItemAddArg, (id) => !string.IsNullOrEmpty(id)));

        #endregion
        #region AutoScan

        private Command _autoScanCommand;

        public Command AutoScanCommand => _autoScanCommand ?? (_autoScanCommand = new Command(ExecuteAutoScan));

        #endregion
        #endregion
        #region Removing

        private Command _removeOneCommandArg;

        public Command RemoveOneCommandArg => _removeOneCommandArg ?? (_removeOneCommandArg = new Command<IBasketRecord>(ExecuteRemoveOne));

        private Command _removeAllCommandArg;

        public Command RemoveAllCommandArg => _removeAllCommandArg ?? (_removeAllCommandArg = new Command<IBasketRecord>(ExecuteRemoveAll));

        #endregion
        #region Adjust

        private Command _adjustCommandArg;

        public Command AdjustCommandArg => _adjustCommandArg ?? (_adjustCommandArg = new Command<BasketItem>(ExecuteAdjustItem));

        #endregion
        #region Return

        private Command _returnCommandArg;

        public Command ReturnCommandArg => _returnCommandArg ?? (_returnCommandArg = new Command<BasketItem>(ExecuteReturn));

        private Command _revertReturnCommandArg;

        public Command RevertReturnCommandArg => _revertReturnCommandArg ?? (_revertReturnCommandArg = new Command<BasketReturnItem>(ExecuteRevertReturn));

        #endregion
        #endregion
        #region Transactions
        #region Alter

        private Command _alterTransactionSelectorCommand;

        public Command AlterTransactionSelectorCommand => _alterTransactionSelectorCommand ?? (_alterTransactionSelectorCommand = new Command(ExecuteAlterTransactionSelector));

        private Command _alterTransactionCommand;

        public Command AlterTransactionCommand => _alterTransactionCommand ?? (_alterTransactionCommand = new Command<int>(ExecuteAlterTransaction));

        #endregion
        #region Store

        private Command _storeTransactionCommand;

        public Command StoreTransactionCommand => _storeTransactionCommand ?? (_storeTransactionCommand = new Command(ExecuteStoreTransaction));

        #endregion
        #region Retrieve

        private Command _retrieveTransactionCommand;

        public Command RetrieveTransactionCommand => _retrieveTransactionCommand ?? (_retrieveTransactionCommand = new Command(ExecuteRetrieveTransaction));

        #endregion
        #region Checkout

        private Command _checkoutTransactionCommand;

        public Command CheckoutTransactionCommand => _checkoutTransactionCommand ?? (_checkoutTransactionCommand = new Command(ExecuteCheckoutTransaction));

        #endregion
        #region Cancel

        private Command _cancelTransactionCommand;

        public Command CancelTransactionCommand => _cancelTransactionCommand ?? (_cancelTransactionCommand = new Command(ExecuteCancelTransaction));

        #endregion
        #endregion

        #endregion

        #region Command Execution
        #region Items
        #region Add
        private async void ExecuteItemAdd()
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                var item = await FindItem(ItemId);
                if (item == null)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), "ItemNotFoundMesg".Translate(), "OK".Translate());
                    return;
                }

                BasketItem tempItem;

                if (SelectedBasketRecord != null &&
                    (SelectedBasketRecord is BasketItem) &&
                    ((BasketItem)SelectedBasketRecord).Item.Id.Equals(item.Id) &&
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

                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Added To Basket");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void ExecuteItemAddArg(string id)
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                var item = await FindItem(id);
                if (item == null)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), "ItemNotFoundMesg".Translate(), "OK".Translate());
                    Logger.LogEvent(AppLogLevel.Warn, $"{this.GetType().Name}: Item Added To Basket (from Request)",
                        new Dictionary<string, string> {{"Success", "False"}, {"Reason", "Item no longer exists"}});
                    return;
                }

                BasketItem tempItem;

                if (SelectedBasketRecord != null &&
                    (SelectedBasketRecord is BasketItem) &&
                    ((BasketItem)SelectedBasketRecord).Item.Id.Equals(item.Id) &&
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

                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Added To Basket (from Request)",
                    new Dictionary<string, string> {{"Success", "True"}});
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
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                if (basketRecord.Quantity > 1)
                    basketRecord.Quantity--;
                else
                {
                    Basket.Remove(basketRecord);
                    if (SelectedBasketRecord == basketRecord)
                        SelectedBasketRecord = null;
                }
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
                if (SelectedBasketRecord == basketRecord)
                    SelectedBasketRecord = null;
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion
        #region Adjust
        /// <summary>
        /// Ask a SECOND person to authorise something this operator cannot.
        ///
        /// ⚠ REPLACES `Authorisation.RequestAuthorisedUserInput`, which is not ported and must not
        /// be: it declares `string authEmpId = default`, loops `while (string.IsNullOrEmpty(authEmpId))`
        /// and **never assigns it** — so correct credentials re-prompt indefinitely and the only
        /// exit is Cancel. Its caller then re-checked the ORIGINAL operator anyway, so the
        /// authoriser was collected and thrown away.
        ///
        /// ⚠ The real rule lives in `OperatorLogin.AuthoriseOverrideAsync`: it refuses
        /// self-authorisation, applies the SUPERVISOR's own ceiling, window and staleness tier —
        /// nothing about being an override relaxes any of it — and names both people.
        /// </summary>
        private async Task<bool> RequestSupervisorOverrideAsync(string permission, long? amountPence)
        {
            try
            {
                var requestedBy = App.GetViewModel().SignedInOperator;
                if (requestedBy is null) return false;   // nobody to attribute the request to

                var credentials = await Helpers.Security.SupervisorPrompt.AskAsync();
                if (credentials is null) return false;   // cancelled — the basket is untouched

                var login = new Plutus.Client.Core.OperatorLogin(
                    new Services.Connectivity.FileOperatorStore());

                var result = await login.AuthoriseOverrideAsync(
                    requestedBy, credentials.Value.EmailOrId, credentials.Value.Password,
                    permission, amountPence);

                if (!result.Succeeded)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), result.Message, "OK".Translate());
                    return false;
                }

                Logger.LogEvent(AppLogLevel.Info, $"{GetType().Name}: Supervisor override",
                    new Dictionary<string, string>
                    {
                        { "Permission", permission },
                        { "RequestedBy", requestedBy.UserId.ToString() },
                        { "AuthorisedBy", result.Granted!.AuthorisedByUserId.ToString() },
                        { "AuthorisedByName", result.Granted.AuthorisedByName },
                        { "AmountPence", amountPence?.ToString() ?? "n/a" },
                    });

                return true;
            }
            catch (Exception ex)
            {
                // ⚠ An override that errors is an override that did NOT happen.
                CrashLog.Write("TillViewModel.RequestSupervisorOverrideAsync", ex);
                return false;
            }
        }

        private async void ExecuteAdjustItem(BasketItem basketItem)
        {
            if (IsBusy) return;

            // ⚠ THIS HAD NO PERMISSION CHECK AT ALL. Anyone who could reach the till could retype
            // any line's price to anything, with nothing recorded about who did it.
            var priceGate = Services.Security.TillGate.Check(
                App.GetViewModel().SignedInOperator, PermissionCatalogue.PosPriceOverride);

            if (!priceGate.Allowed)
            {
                if (!priceGate.NeedsOverride ||
                    !await RequestSupervisorOverrideAsync(PermissionCatalogue.PosPriceOverride, null))
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), priceGate.Message, "OK".Translate());
                    return;
                }
            }

            IsBusy = true;
            try
            {
                const NumberStyles numberStyles = NumberStyles.AllowCurrencySymbol | NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign;
                IValidator[] validators = {
                    new RequiredValidator(),
                    new CurrencyValueValidator(numberStyles)
                };
                ViewElementData[] elements = {
                    new ViewElementData(1, "PriceExTax".Translate(), basketItem.PriceExTax.ToString("C", CultureInfo.CurrentCulture), validators.AsEnumerable(), false, true),
                    new ViewElementData(2, "Price".Translate(), basketItem.Price.ToString("C", CultureInfo.CurrentCulture), validators.AsEnumerable(), false, true)
                };

                var data = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(elements, "Confirm".Translate(), true, "Adjust".Translate());

                _ = data.TryGetValue(1, out string priceExTax);
                _ = data.TryGetValue(2, out string priceTax);
                if (priceExTax != null && priceTax != null)
                {
                    basketItem.PriceExTax = decimal.Parse(priceExTax, numberStyles, CultureInfo.CurrentCulture);
                    basketItem.Price = decimal.Parse(priceTax, numberStyles, CultureInfo.CurrentCulture);
                }
                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Adjustment");
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
                IValidator[] stringValidators = {
                    new RequiredValidator()
                };

                ViewElementData[] elements = {
                    new ViewElementData(1, string.Format("IdArg".Translate(), "Sale".Translate()), "", stringValidators, false, true),
                    new ViewElementData(2, "Reason".Translate(), "ReturnReasonExample".Translate(), stringValidators, false, true)
                };
                #endregion

                var returnItem = basketItem.Adapt<BasketReturnItem>();

                bool continueLoop;
                do
                {
                    continueLoop = false;
                    var alertReturnValues = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(elements, "Confirm".Translate(), true, "Returns".Translate(), "Cancel".Translate());
                    if (!alertReturnValues.TryGetValue(1, out string saleIdText) &
                        !alertReturnValues.TryGetValue(2, out string reasonText))
                    {
                        Logger.LogError(new ArgumentException(
                                $"{this.GetType().Name}: {nameof(alertReturnValues)} does not have the expected key required, to move forward!"),
                            new Dictionary<string, string>
                                {{"alertReturnValues", string.Join(Environment.NewLine, alertReturnValues)}});
                        await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                            "CriticalErrorReportedMesg".Translate(),
                            "OK".Translate());
                        return;
                    }

                    if (string.IsNullOrEmpty(saleIdText) || string.IsNullOrEmpty(reasonText))
                    {
                        continueLoop = true;
                        continue;
                    }

                    Enum.TryParse(DatabaseProviderSetting, out DatabaseProvider databaseProvider);
                    using (var db = new Helpers.Database.Database(databaseProvider))
                    {
                        if (!db.IsExists<SaleModel, string>(saleIdText))
                        {
                            continueLoop = true;
                            await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), "SaleIDWrongMesg".Translate(), "OK".Translate());
                            continue;
                        }

                        var trans = db.Get<TransactionModel>()
                            .Include(t => t.Sale)
                                .ThenInclude(s => s.Refunded)
                            .Include(t => t.CheckoutItemChange)
                            .Where(t => t.SaleId.Equals(saleIdText) && t.ItemId.Equals(basketItem.Item.Id));
                        TransactionModel tran = null;

                        if (trans.Count() > 1)
                        {
                            foreach (var tempTran in trans)
                            {
                                if ((tempTran.CheckoutItemChange == null ||
                                     tempTran.CheckoutItemChange.Price != basketItem.Price) &&
                                    (tempTran.ItemCostPrice != basketItem.Price ||
                                     tempTran.ItemCostExPrice != basketItem.PriceExTax)) continue;
                                tran = tempTran;
                                break;
                            }
                        }
                        else
                            tran = trans.First();

                        if (tran == null)
                        {
                            continueLoop = true;
                            await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), "ItemNotExistInSaleMesg".Translate(), "OK".Translate());
                            continue;
                        }

                        if (tran.Amount < basketItem.Quantity)
                        {
                            await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), string.Format("ItemAmountExceedsMesg".Translate(), basketItem.Name, tran.Amount, basketItem.Quantity - tran.Amount), "OK".Translate());
                            return;
                        }
                        else
                        {
                            var refundsLeft = tran.Amount;
                            if (tran.Sale.Refunded != null)
                            {
                                refundsLeft -= tran.Sale.Refunded.Where(r => r.ItemId.Equals(basketItem.Item.Id)).Sum(r => r.Amount);
                            }

                            if (refundsLeft < basketItem.Quantity)
                            {
                                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), string.Format("NoRefundsLeftMesg".Translate(), refundsLeft, basketItem.Name), "OK".Translate());
                                return;
                            }

                            returnItem.PriceExTax = tran.CheckoutItemChange?.ExPrice ?? tran.ItemCostExPrice;
                            returnItem.Price = tran.CheckoutItemChange?.Price ?? tran.ItemCostPrice;
                            returnItem.SetItemReturn(reasonText, saleIdText);
                        }
                    }
                } while (continueLoop);

                //finalize change
                Basket.Remove(basketItem);
                Basket.Add(returnItem);
                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Return");
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
                var basketItem = basketReturnItem.Adapt<BasketItem>();

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

                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Transaction Alteration (Discounts)");
                Enum.TryParse(DatabaseProviderSetting, out DatabaseProvider databaseProvider);

                // ⚠ `App.GetViewModel().EmployeeId` used to be passed here and it CRASHED THE APP on
                // a portal-provisioned till: the property threw on an empty legacy roster, out of a
                // plain `void` command handler, straight through Button.Clicked to the UI thread.
                // Tapping the leftmost button on the till screen closed the application. It now
                // returns null, and the audit user is the SIGNED-IN OPERATOR, which is the person
                // who actually applied the discount on either sign-in path.
                var auditUser = App.GetViewModel().SignedInOperator?.UserId.ToString()
                                ?? App.GetViewModel().EmployeeId;

                using (var db = new Helpers.Database.Database(databaseProvider, auditUser))
                {
                    foreach (var discount in db.Get<DiscountModel>())
                        Alterations.Add(discount);
                }

                // ⚠ THE PICKER MUST NOT OPEN EMPTY. `SfPicker.SelectedIndex` on a column with no
                // rows is 0, not null, so the view's SelectionChanged fires
                // `AlterTransactionCommand.Execute(0)` and `Alterations.ElementAt(0)` throws
                // ArgumentOutOfRangeException in another `async void` — an empty dialog whose OK
                // button closes the app. The legacy Discounts table is empty on a portal till and
                // was never seeded even on legacy ones.
                if (Alterations.Count == 0)
                {
                    Application.Current.MainPage.DisplayAlert(
                        "Hmm".Translate(),
                        "There are no discounts set up for this till yet.",
                        "OK".Translate());
                    return;
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
                // ⚠ THIS HAD NO PERMISSION CHECK AT ALL, and it is the one place on the till where
                // an operator types a money amount straight off the goods — a cash discount or a
                // percentage, unbounded. `pos.discount` is ceiling-capable precisely so it can be
                // handed out with a limit; nothing was asking for it.
                var discountGate = Services.Security.TillGate.Check(
                    App.GetViewModel().SignedInOperator, PermissionCatalogue.PosDiscount);

                if (!discountGate.Allowed &&
                    (!discountGate.NeedsOverride ||
                     !await RequestSupervisorOverrideAsync(discountGate.Permission, discountGate.AmountPence)))
                {
                    await Application.Current.MainPage.DisplayAlert(
                        "Hmm".Translate(), discountGate.Message, "OK".Translate());
                    return;
                }

                var alteration = Alterations.ElementAt(selectedAlteration);
                var items = new List<BasketItem>();

                foreach (var item in Basket.Where(br => br is BasketItem).Cast<BasketItem>().ToList())
                {
                    for (var i = 0; i < item.Quantity; i++)
                        items.Add((BasketItem)item.Clone());
                }

                var entries = new ViewElementData[1];

                if (alteration.Amount == 0.0m)
                    entries[0] = new ViewElementData(1,
                        alteration.Type == 0 ? "Cash".Translate() : "Percent".Translate(), "0", new List<IValidator>(), false, true);

                else
                    entries[0] = new ViewElementData(1,
                        alteration.Type == 0 ? "Cash".Translate() : "Percent".Translate(), alteration.Amount.ToString(CultureInfo.CurrentCulture), new List<IValidator>(), false, false);

                //data type -> Tuple<List<string>, List<BasketItem>>
                var (alterationAmounts, applyAlterationsToBasketItems) = await Helpers.CustomViews.InputMultiSelectAlertHelper<string, BasketItem>.LaunchInputMulitSelectAlertAsync(entries, Tuple.Create<IEnumerable<BasketItem>, string>(items, "Name"), default, "Confirm".Translate(), false, "Alterations".Translate());

                BasketAlteration adjustment;

                if (applyAlterationsToBasketItems.Count() < items.Count())
                {
                    foreach (var item in applyAlterationsToBasketItems)
                    {
                        if (alteration.Type == 0)
                        {
                            var alterationAmount = Math.Abs(Math.Round(Decimal.Parse(alterationAmounts.First()), 2, MidpointRounding.AwayFromZero)) * -1;
                            adjustment = new BasketAlteration(new NoteModel($"{alteration.Name}, {item.Name} {alterationAmount.ToString("C2", CultureInfo.CurrentCulture)}"), alteration, item, alterationAmount, alterationAmount);

                        }
                        else
                        {
                            var alterationAmount = Tuple.Create(Math.Abs(Math.Round(item.Price * Decimal.Parse(alterationAmounts.First()), 2, MidpointRounding.AwayFromZero)) * -1, Math.Abs(Math.Round(item.PriceExTax * Decimal.Parse(alterationAmounts.First()), 2, MidpointRounding.AwayFromZero)) * -1);
                            adjustment = new BasketAlteration(new NoteModel($"{alteration.Name}, {item.Name} {alterationAmount.Item1.ToString("C2", CultureInfo.CurrentCulture)}"), alteration, item, alterationAmount.Item1, alterationAmount.Item2);
                        }
                        Basket.Add(adjustment);
                    }
                }
                else
                {
                    if (alteration.Type == 0)
                    {
                        var alterationAmount = Math.Abs(Math.Round(Decimal.Parse(alterationAmounts.First()) * applyAlterationsToBasketItems.Count(), 2, MidpointRounding.AwayFromZero)) * -1;
                        adjustment = new BasketAlteration(new NoteModel($"{alteration.Name}, {alterationAmount.ToString("C2", CultureInfo.CurrentCulture)}"), alteration, applyAlterationsToBasketItems, alterationAmount, alterationAmount);
                    }
                    else
                    {
                        var alterationAmount = Tuple.Create(Math.Abs(Math.Round(applyAlterationsToBasketItems.Sum(tempItem => tempItem.Price) * Decimal.Parse(alterationAmounts.First()), 2, MidpointRounding.AwayFromZero)) * -1, Math.Abs(Math.Round(applyAlterationsToBasketItems.Sum(tempItem => tempItem.PriceExTax) * Decimal.Parse(alterationAmounts.First()), 2, MidpointRounding.AwayFromZero)) * -1);
                        adjustment = new BasketAlteration(new NoteModel($"{alteration.Name}, {alterationAmount.Item1.ToString("C2", CultureInfo.CurrentCulture)}"), alteration, applyAlterationsToBasketItems, alterationAmount.Item1, alterationAmount.Item2);
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
                IValidator[] validators = {
                    new RequiredValidator()
                };

                ViewElementData[] elements = {
                    new ViewElementData(1, "Name".Translate(), "", validators.AsEnumerable(), false, true)
                };

                string transName;
                var firstRun = true;
                do
                {
                    if (!firstRun)
                        await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), "NameMustBeUniqueMesg".Translate(), "OK".Translate());

                    _ = (await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(elements, "Confirm".Translate(), false, "TransactionName".Translate(), "Cancel".Translate())).TryGetValue(1, out transName);

                    if (string.IsNullOrEmpty(transName))
                    {

                        Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Transaction Store (Saving)", new Dictionary<string, string> { { "Canceled", "True" } });
                        return;
                    }
                    firstRun = false;
                } while (StoredTransactions.Any(sT => sT.Name.Equals(transName)));


                var basketRecords = new IBasketRecord[Basket.Count];

                Basket.CopyTo(basketRecords, 0);

                StoredTransactions.Add(
                    new SavedTransactionModel
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = transName,
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
                        await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), "CriticalIssue".Translate(), "OK".Translate());
                        StoredTransactions.RemoveAt(StoredTransactions.Count());
                        return;
                    }
                }
                Basket.Clear();

                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Transaction Store (Saving)", new Dictionary<string, string> { { "Canceled", "False" } });
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
                    var action = await Application.Current.MainPage.DisplayActionSheet("Baskets".Translate(), "Cancel".Translate(), null, baskets);
                    if (action == "Cancel".Translate())
                    {
                        Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Transaction Retrieved", new Dictionary<string, string> { { "Canceled", "True" } });
                        return;
                    }
                    storedTransaction = StoredTransactions.First(sT => sT.Name.Equals(action));
                }
                else
                {
                    storedTransaction = StoredTransactions.First();
                }

                if (Basket.Count > 0)
                    if (!await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), "BasketWillBeClearedMesg".Translate(), "OK".Translate(), "Cancel".Translate()))
                        return;

                Enum.TryParse(DatabaseProviderSetting, out DatabaseProvider databaseProvider);
                using (var db = new Helpers.Database.Database(databaseProvider))
                {
                    db.Delete(new SavedTransactionModel { Id = storedTransaction.Id });
                    if (!db.Save())
                    {
                        await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), "CriticalIssue".Translate(), "OK".Translate());
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
                if (basket != null)
                    foreach (var item in basket)
                        Basket.Add(item);

                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Transaction Store (Saving)", new Dictionary<string, string> { { "Canceled", "False" } });
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion

        private async void ExecuteCheckoutTransaction()
        {

            if (IsBusy) return;

            using var activity = Plutus.Frontend.AppClient.Services.Analytics.Observability.ActivitySource.StartActivity("Till.CheckoutTransaction");

            IsBusy = true;
            try
            {
                // ⚠ THIS LINE USED TO READ `App.GetViewModel().EmployeeId` AND IT CLOSED THE APP.
                // That property is `Employees.Last().Id`, and `Employees` is populated ONLY by the
                // legacy local login — the portal roster path sets `SignedInOperator` and never
                // touches it. So on every portal-provisioned till the read threw
                // `InvalidOperationException: Sequence contains no elements`, from an `async void`
                // with no catch, BEFORE the first await — which reposts to the UI thread as an
                // unhandled exception and terminates the process. Pressing Checkout killed the till
                // mid-sale, with a full basket and a customer at the counter, and no dialog.
                //
                // ⚠ And it fed NOTHING. The value was set on a `SaleModel` that step 11 stopped
                // persisting; the sale is attributed from `SignedInOperator.UserId` at commit. A
                // read with no consumer was the single thing preventing any sale on any new till.
                var sale = new SaleModel
                {
                    DateOfSale = DateTime.Now,
                    Total = 0.0m,
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

                    var payMeth = await Application.Current.MainPage.DisplayActionSheet("PayMeth".Translate(), "Cancel".Translate(), null, payMethNames);

                    if (payMeth == "Cancel".Translate())
                    {
                        Logger.LogEvent(AppLogLevel.Info, "Sale Processing", new Dictionary<string, string> { { "Canceled", "True" } });
                        return;
                    }

                    var pay = new PaymentMethod_SaleModel() { TempPayMethod = payMeths[payMeth]() };

                    // ⚠ THE SURCHARGE IS THE TENANT'S GATEWAY SETTING, NOT THE LEGACY
                    // `PaymentMethod.Charge`. The legacy field lives on a GLOBAL table — one
                    // tenant's fee would have been every tenant's fee — and its path here was
                    // broken twice over: the resource key was misspelt (`CardChangeNote` for
                    // `CardChargeNote`, an ArgumentException in debug and a literal key on the
                    // receipt in release), and the `BasketNote` it added carries money the platform
                    // sale cannot represent, so the commit guard refuses the basket.
                    //
                    // ⚠ A REAL LINE against the provisioned CARD-SURCHARGE item, priced by the
                    // shared rules: the fee is further consideration for the main supply (Bookit
                    // C-607/14 / NEC C-130/15), so its VAT FOLLOWS THE BASKET — zero on zero-rated
                    // goods, blended on a mixed basket, never a hardcoded rate. Applied ONCE per
                    // sale (a split payment must not charge the flat fee twice), only to card
                    // tenders, and never to refunds.
                    if (SharedKernel.Tenders.FromMethodName(pay.TempPayMethod.Name) == SharedKernel.Tenders.Card
                        && !refundOnly
                        && !Services.Storage.CheckoutCommit.HasSurcharge(Basket))
                    {
                        var (surchargeBp, surchargeFlat) = await Services.Storage.GatewaySurcharge.GetAsync();
                        var feeLine = Services.Storage.CheckoutCommit.SurchargeItem(Basket, surchargeBp, surchargeFlat);
                        if (feeLine != null)
                        {
                            Basket.Add(feeLine);
                            sale.Total = Basket.Sum(bR => bR.Price * (bR is BasketReturnItem ? -1 : 1) * bR.Quantity);
                        }
                    }
                    #region Setup and run payment amount input
                    const NumberStyles testStyles = NumberStyles.AllowCurrencySymbol | NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign;
                    IValidator[] validators = {
                            new RequiredValidator(),
                            new CurrencyValueValidator(testStyles)
                    };

                    ViewElementData[] elements = {
                        new ViewElementData(1, "Amount", "", validators.AsEnumerable(), false, true)
                    };

                    _ = (await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                        elements,
                        "Confirm".Translate(),
                        false,
                        !pay.TempPayMethod.IsCashBackable || (pay.TempPayMethod.IsChangeable),
                        sale.Total - paid,
                        string.Format(
                            refundOnly ? "HowMuchRefund".Translate() : "HowMuchPM".Translate(),
                            payMeth,
                            Math.Round(sale.Total - paid, 2, MidpointRounding.AwayFromZero)))).TryGetValue(1, out var amountText);

                    if (amountText != null)
                    {
                        var amount = decimal.Parse(amountText, testStyles, CultureInfo.CurrentCulture);
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
                    }

                    sale.PaySales.Add(pay);
                }

                if (sale.PaySales.Any(p => p.TempPayMethod.IsCashBackable) && CashbackEnabled)
                {
                    //Cash-back stuff here
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

                if (!@continue) return;

                //Prepare sale for Transaction and Refunds adding
                sale.Transactions = new List<TransactionModel>();
                sale.Refunds = new List<RefundModel>();

                //Loop through all BasketItems in Basket
                foreach (var item in Basket.Where(bR => bR is BasketItem).Cast<BasketItem>().ToList())
                {
                    var tran = new TransactionModel() { ItemId = item.Item.Id, Sale = sale, Amount = item.Quantity, ItemCostExPrice = item.Item.ExPrice, ItemCostPrice = item.Item.Price, Transaction_Discounts = new ObservableCollection<TransactionModel_DiscountModel>() };
                    if (Basket.Where(bR => bR is BasketAlteration).Cast<BasketAlteration>().Any())
                    {
                        var tempIA = Basket.Where(bR => bR is BasketAlteration && !(bR is BasketReturnItem)).Cast<BasketAlteration>().FirstOrDefault(bA => bA.ItemsAssocitated.Any(iA => iA.Item.Id.Equals(item.Item.Id)));
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

                // ⚠ ONE GATE, AGAINST THE OPERATOR'S OWN CEILING (cutover step 12). What was here
                // could not work on a portal-provisioned till and had a hole in it besides:
                //
                //   · it looked up string action names ("Till", "Refund20", "Refund100") in a local
                //     AuthActions table that such a till does not have;
                //   · the hardcoded £20/£100/unlimited bands ignored each operator's actual ceiling;
                //   · ⚠ the refund total summed each return line's UNIT price and IGNORED QUANTITY,
                //     so five £30 returns tested as £30 and went straight through the £100 band;
                //   · and the do/while "escalation" re-tested the SAME operator every pass while
                //     RequestAuthorisedUserInput never assigned the id it returned — so entering
                //     correct supervisor credentials re-prompted for ever and only Cancel escaped.
                //     Supervisor override on this till has never once succeeded.
                //
                // ⚠ A NULL operator BLOCKS. "We don't know who this is" must never mean "let them".
                var gate = Services.Security.TillGate.CheckCheckout(
                    App.GetViewModel().SignedInOperator, Basket);

                if (!gate.Allowed)
                {
                    if (!gate.NeedsOverride)
                    {
                        await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), gate.Message, "OK".Translate());
                        return;
                    }

                    // ⚠ A real override: a SECOND person authenticates, and OperatorLogin refuses
                    // self-authorisation, applies the SUPERVISOR's own ceiling and window, and names
                    // both people for the audit trail. Declining leaves the basket untouched.
                    //
                    // ⚠ ESCALATE WHAT THE GATE ACTUALLY REFUSED. `CheckCheckout` tests `pos.sell`
                    // FIRST, so hardcoding "refund" here asked a supervisor to authorise a £0.00
                    // refund — a basket with no returns refunds nothing — and then let an ordinary
                    // sale through on the strength of it, nobody having been asked whether this
                    // operator may sell. Any supervisor holding `pos.refund`, including one
                    // explicitly denied `pos.sell`, would have waved it through.
                    if (!await RequestSupervisorOverrideAsync(gate.Permission, gate.AmountPence))
                        return;
                }

                FinaliseTransation(sale, change);
                return;
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
            {
                var itemHasNoStock = false;

                // ⚠ THE PER-LINE STOCK DECREMENT IS GONE (cutover step 11). It read a legacy
                // StockModel and called db.Save() ONCE PER LINE with no transaction, so a crash
                // halfway through a basket left some lines decremented and some not, with nothing
                // to reconcile against. v2 holds no local stock at all: the SERVER attributes
                // movement from `LineMeta.itemIdOne` on the sale it receives, which is one
                // authority instead of one per till.

                // ⚠ COMMIT BEFORE PRINTING, and this ordering is the whole point of the block.
                // The receipt is printed from what was COMMITTED — so a printer failure is a
                // reprint problem, never a money problem. The reverse order loses a sale that a
                // customer has already been handed a receipt for.
                var tenders = Services.Storage.CheckoutCommit.TendersFrom(
                    sale.PaySales.Select(p => ((string?)p.TempPayMethod?.Name, p.Amount, p.Change)));

                var outcome = await Services.Storage.CheckoutCommit.CommitAsync(
                    Basket, tenders, App.GetViewModel().SignedInOperator?.UserId);

                if (!outcome.Committed)
                {
                    // ⚠ The basket is deliberately NOT cleared. Nothing was recorded, so the sale
                    // is still there to retry — clearing it would lose the sale and the evidence.
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), outcome.Message, "OK".Translate());
                    return;
                }

                var tasks = new Task[3];

                var printerMgr = new PosPrinterManager();

                var trackEventArgs = new Dictionary<string, string>
                {
                    { "Canceled", "False" }
                };

                if (DeviceInfo.Idiom == DeviceIdiom.Desktop)
                {
                    if (!AskForReceipt || await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), "ReceiptRequired".Translate(), "Yes".Translate(), "No".Translate()))
                    {
                        trackEventArgs.Add("Receipt Requested", "True");
                        // ⚠ Built from the COMMITTED payload, not from `sale` — the receipt states
                        // what the platform accepted, and its barcode carries the platform saleId
                        // (the legacy `sale.Id` has been empty since step 11 removed the save that
                        // assigned it, so every receipt printed a blank barcode).
                        var receipt = Services.Printing.ReceiptSale.From(
                            outcome.Request,
                            sale.PaySales.Select(p => p.TempPayMethod?.Name).ToList(),
                            sale.Notes.Select(n => n.Note?.Note).Where(n => !string.IsNullOrWhiteSpace(n)).ToList());

                        tasks[0] = Task.Run(async () =>
                        {
                            _ = await printerMgr.InitPrinter();
                            await printerMgr.SetUpSalePrint(receipt, Basket, App.GetViewModel().Store);
                            await printerMgr.ExecuteOposOrPdfAsync();

                            trackEventArgs.Add("Receipt Printed Successfully", "True");
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
                    tasks[2] = Application.Current.MainPage.DisplayAlert("Hmm".Translate(), string.Format("CashBack".Translate(), change), "OK".Translate());
                }

                try
                {
                    await Task.WhenAll(tasks.Where(t => t != null));
                    _ = await printerMgr.CloseConnection();
                    printerMgr.Dispose();
                }
                catch (POSObjectException pOSObjectException)
                {
                    Logger.LogError(pOSObjectException);
                    switch (pOSObjectException.POSTargetObjectType)
                    {
                        case CommonPOSLibrary.Enums.POSTargetObjectType.Printer:
                            trackEventArgs.Add("Receipt Printed Successfully", "False");
                            await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), "There was a problem with the POS Printer. Transaction has succeeded but a receipt is currently unavailable.", "OK".Translate());
                            break;
                        case CommonPOSLibrary.Enums.POSTargetObjectType.CashDrawer:
                            trackEventArgs.Remove("Cash Drawer Opened Successfully");
                            trackEventArgs.Add("Cash Drawer Opened Successfully", "False");
                            CashDrawerWarningSilenced = !await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), "CashDrawerErrorWarning".Translate(), "OK".Translate(), "Silence".Translate());
                            break;
                    }
                }
                catch (POSPrinterException pOSPrinterException)
                {
                    if (pOSPrinterException.POSPrinterExceptionType == CommonPOSLibrary.Enums.POSPrinterExceptionType.PrinterNotClaimed)
                    {
                        Logger.LogError(pOSPrinterException);
                    }
                    trackEventArgs.Add("Receipt Printed Successfully", "False");
                    trackEventArgs.Add("Printed Not Selected", "True");
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), "There is no POS Printer selected. Transaction has succeeded but a receipt is currently unavailable.", "OK".Translate());
                }
                Basket.Clear();
                await Application.Current.MainPage.DisplayAlert("Transaction".Translate(), "TransConfMesg".Translate(), "OK".Translate());

                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Sale Processing", trackEventArgs);

                if (!itemHasNoStock)
                {
                    return;
                }
                //put in stock warning
            }
        }

        /// <summary>
        /// The ways this till can take money (cutover step 13b).
        ///
        /// ⚠ THIS USED TO READ THE LEGACY `PaymentMethodModel` TABLE, AND THAT STOPPED A NEW TILL
        /// SELLING AT ALL. The table is seeded only by `Database.Init()` — the legacy first-run
        /// path — which a portal-provisioned till never runs, and it has no local database anyway.
        /// So the payment sheet rendered ZERO buttons and the `paid != sale.Total` loop above could
        /// never terminate: the operator was stuck in a checkout with nothing to press but Cancel,
        /// with a customer in front of them. Every dev machine hid it, because they were migrated
        /// from legacy installs that had already seeded Card and Cash.
        ///
        /// The tenders now come from the FIXED shared set (binding default 13). The legacy model is
        /// still the carrier because the rest of this checkout reads `IsChangeable`/`IsCashBackable`
        /// off it; step 11b retires the model itself. ⚠ `Charge`/`MinimumCharge` stay ZERO — the
        /// card surcharge is the TENANT's gateway setting now, not a per-row legacy field on a
        /// GLOBAL table where one client's fee would have been every client's.
        /// </summary>
        private Dictionary<string, Func<PaymentMethodModel>> GenPaymentMethodActions()
        {
            var refundOnly = !Basket.Any(bR => bR is BasketItem && !(bR is BasketReturnItem));

            return Services.Sales.TillTenders.Offered(refundOnly).ToDictionary<
                Services.Sales.TillTender, string, Func<PaymentMethodModel>>(
                t => t.Name,
                t => () => new PaymentMethodModel
                {
                    Name = t.Name,
                    IsChangeable = t.GivesChange,
                    IsCashBackable = t.GivesCashback,
                    Charge = 0m,
                    MinimumCharge = 0m,
                });
        }

        /// <summary>
        /// Resolve a scanned or typed code against the V2 CATALOGUE (cutover step 10).
        ///
        /// ⚠ THIS FIXES TWO LIVE DEFECTS, not just a data source.
        ///   1. `Database.SearchId` matched on `Id` alone and **did not honour tombstones**, so an
        ///      item the portal had BINNED was still sellable on this till — indefinitely, because
        ///      nothing local ever learned it had gone. `FindByBarcodeAsync` excludes `Removed`,
        ///      which is precisely why the changes feed carries tombstones rather than upserts.
        ///   2. The price came from a stored column, so a price scheduled for 02:00 only applied if
        ///      a sync happened to land after it. It now comes from `EffectivePricePairAsync`,
        ///      evaluated at LOOKUP time against the effective-dated timeline — the change lands on
        ///      the minute on a till that has been offline for a week.
        ///
        /// ⚠ IT STILL RETURNS AN `ItemModel`, AND THAT IS TEMPORARY SCAFFOLDING. The basket holds
        /// `BasketItem.Item` as a legacy entity, and reshaping that means reshaping
        /// `BasketReturnItem`, ~14 call sites, both Mapster configs and the template selector — all
        /// of which have to land WITH `CommitSaleAsync` in step 11, or the till would build v2
        /// baskets and still save legacy sales, which is a worse half-state than either end.
        /// Only Id/Name/Price/ExPrice/Vat.Name are ever read off this object (verified by grep), so
        /// nothing needs the navigations the legacy query used to Include.
        /// **Step 11 deletes this projection.**
        /// </summary>
        private async Task<ItemModel> FindItem(string needle = "")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(needle)) return null;

                var found = await Services.Storage.TillStoreAccess.TryUseAsync(
                    s => s.FindByBarcodeAsync(needle.Trim()));
                if (found == null) return null;

                var price = await Services.Storage.TillStoreAccess.TryUseAsync(
                    s => s.EffectivePricePairAsync(found.Id));

                // The band's display name, for the Tax column. ⚠ Null is a legitimate answer — the
                // portal may not have decided which band this tax row means — and it must render as
                // blank rather than being guessed at.
                var bandName = await Services.Storage.VatBands.DisplayNameForItemAsync(found.Id);

                return new ItemModel
                {
                    // ⚠ IdOne, not the GUID: every legacy screen and the basket key on this string,
                    // and it IS the barcode.
                    Id = found.IdOne,
                    Name = found.Name,
                    // ⚠ Pence → decimal pounds ONLY because the legacy model is decimal. Deliberately
                    // inline rather than a SharedKernel helper: money is integer pence end-to-end
                    // (architecture §4.1) and a shared pence→decimal converter would legitimise the
                    // conversion everywhere instead of confining it to this scaffolding.
                    Price = price.IncPence / 100m,
                    ExPrice = price.ExPence / 100m,
                    Vat = new TaxModel { Name = bandName ?? string.Empty },
                };
            }
            catch (Exception ex)
            {
                Services.Analytics.CrashLog.Write("TillViewModel.FindItem", ex);
                return null;
            }
        }
        #endregion

        #region INotifyPropertyChanged
        private void BasketRecordOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(Basket));
            OnPropertyChanged(nameof(SaleExTax));
            OnPropertyChanged(nameof(SaleIncTax));
        }
        #endregion
    }
}