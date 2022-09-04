using CommonPOSLibrary.Exceptions;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Plutus.Entities.Models;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Core.Enum;
using Plutus.Frontend.ClientUI.Core.Models;
using Plutus.Frontend.ClientUI.Domain.Models;
using Plutus.Frontend.ClientUI.Helpers;
using Plutus.Frontend.ClientUI.Pages.MainTill;
using Plutus.Frontend.ClientUI.Pages.PopupViews;
using Plutus.Frontend.ClientUI.Resources.I18N_L10N;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Frontend.ClientUI.Services.Loading;
using Plutus.Frontend.ClientUI.Services.PosHandeling;
using Plutus.Frontend.ClientUI.Services.Repository.Contracts;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

namespace Plutus.Frontend.ClientUI.ViewModels.MainTill
{
    public partial class TillViewModel : BaseViewModel
    {
        #region Fields
        private readonly ObservableCollection<IBasketRecord> _basket;

        [ObservableProperty]
        private bool _isDesktop;
        [ObservableProperty]
        private string _itemId;
        [ObservableProperty]
        private string _defaultBagId;

        [ObservableProperty]
        private IBasketRecord _selectedBasketRecord;

        [ObservableProperty]
        private double quantity;
        #endregion

        #region Properties
        public ObservableCollection<Discount> Alterations { get; }
        public ObservableCollection<IBasketRecord> Basket => Settings.TillListViewOrderReversed ? new ObservableCollection<IBasketRecord>(_basket.Reverse()) : _basket;
        public decimal SalesExTax => Basket.Sum(bR => bR.PriceExTax * (bR is BasketReturnItem ? -1 : 1) * bR.Quantity);
        public decimal SalesIncTax => Basket.Sum(bR => bR.Price * (bR is BasketReturnItem ? -1 : 1) * bR.Quantity);
        public ObservableCollection<SavedTransaction> StoredTransactions { get; }
        #endregion

        public TillViewModel(ILogger logger, IAppState appState, IDeviceInfo deviceInfo, LoadingViewService loadingViewService, IRepositoryWrapper repositoryWrapper) : base(logger, appState, loadingViewService, repositoryWrapper)
        {
            #region Init
            Title = Strings.Till;
            Icon = "\uf788";
            _basket = new ObservableCollection<IBasketRecord>();
            StoredTransactions = new ObservableCollection<SavedTransaction>();
            Alterations = new ObservableCollection<Discount>();
            DefaultBagId = Settings.DefaultBagId;
            Quantity = 1;
            #endregion

            #region Events
            _basket.CollectionChanged += (sender, e) =>
            {
                if (e.NewItems != null)
                    foreach (INotifyPropertyChanged added in e.NewItems)
                        added.PropertyChanged += BasketRecordOnPropertyChanged;
                if (e.OldItems != null)
                    foreach (INotifyPropertyChanged removed in e.OldItems)
                        removed.PropertyChanged -= BasketRecordOnPropertyChanged;
                OnPropertyChanged(nameof(Basket));
                OnPropertyChanged(nameof(SalesExTax));
                OnPropertyChanged(nameof(SalesIncTax));
                OnPropertyChanged(nameof(CanCheckoutAlterOrSave));
            };

            StoredTransactions.CollectionChanged += (sender, e) =>
            {
                OnPropertyChanged(nameof(StoredTransactions));
            };

            Alterations.CollectionChanged += (sender, e) =>
            {
                OnPropertyChanged(nameof(Alterations));
                OnPropertyChanged(nameof(SalesExTax));
                OnPropertyChanged(nameof(SalesIncTax));
                OnPropertyChanged(nameof(CanCheckoutAlterOrSave));
            };

            Settings.StaticPropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(Settings.DefaultBagId))
                {
                    DefaultBagId = Settings.DefaultBagId;
                }
            };
            #endregion

            MessagingCenter.Subscribe<MainPage>(this, "MainUILoaded", async (sender) =>
            {
                foreach (var savedTrans in await RepositoryWrapper.SavedTransactionRepository.GetAll())
                {
                    StoredTransactions.Add(savedTrans);
                }
            });

            MessagingCenter.Subscribe<Inventory.ViewAllInventoryViewModel, string>(this, "AddToBasket", (sender, arg) =>
            {
                AddItem(arg);
            });

            IsDesktop = deviceInfo.Idiom.Equals(DeviceIdiom.Desktop);
        }

        #region Command Can Executes

        private bool CanExecuteItemId() => !string.IsNullOrEmpty(ItemId);
        private bool CanCheckoutAlterOrSave => Basket.Count() > 0;
        #endregion

        #region Commands
        #region Add Item
        [RelayCommand]
        private async void AutoScan()
        {
            throw new NotImplementedException();
        }

        [RelayCommand(CanExecute = nameof(CanExecuteItemId))]
        private async void ManualAdd() => AddItem(ItemId);

        [RelayCommand]
        private async void ManualAddArg(string itemId) => AddItem(itemId);
        #endregion
        #region Removing
        [RelayCommand]
        private void RemoveAll(object basketRecordParameter)
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                if (basketRecordParameter is IBasketRecord basketRecord)
                {
                    _basket.Remove(basketRecord);
                    if (SelectedBasketRecord == basketRecord)
                        SelectedBasketRecord = null;
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private void RemoveOne(object basketRecordParameter)
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                if (basketRecordParameter is IBasketRecord basketRecord)
                {
                    if (basketRecord.Quantity > 1)
                        basketRecord.Quantity--;
                    else
                    {
                        _basket.Remove(basketRecord);
                        if (SelectedBasketRecord == basketRecord)
                            SelectedBasketRecord = null;
                    }
                }
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion
        #region Adjust
        [RelayCommand]
        private async void AdjustItem(object basketItemParameter)
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                if (basketItemParameter is BasketItem basketItem)
                {
                    var retunredValues = await ServiceHelper.GetService<TillPage>().ShowPopupAsync(ServiceHelper.GetService<AdjustItemPage>()) as PopupReturnValue<Tuple<decimal, decimal>>;

                    if (retunredValues.PopupReturnStatus == PopupReturnStatus.Completed)
                    {
                        basketItem.PriceExTax = retunredValues.ReturnValue.Item1;
                        basketItem.Price = retunredValues.ReturnValue.Item2;
                    }
                    Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Adjustment");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion
        #region Return
        [RelayCommand]
        private async void ReturnItem(object basketItemParameter)
        {
            if (IsBusy)
                return;
            IsBusy = true;
            try
            {
                if (basketItemParameter is BasketItem basketItem)
                {
                    var returnedValues = await ServiceHelper.GetService<TillPage>().ShowPopupAsync(ServiceHelper.GetService<ReturnItemPage>()) as PopupReturnValue<Tuple<string, string>>;

                    if (returnedValues.PopupReturnStatus == PopupReturnStatus.Completed)
                    {
                        var returnItem = AppState.Mapper.Map<BasketReturnItem>(basketItem);

                        //Start Auth checking

                        _basket.Remove(basketItem);
                        _basket.Add(returnItem);
                        Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Return");
                    }
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private void RevertReturn(object basketReturnItemParameter)
        {
            if (IsBusy)
                return;

            IsBusy = true;
            try
            {
                if (basketReturnItemParameter is BasketReturnItem basketReturnItem)
                {
                    var basketItem = AppState.Mapper.Map<BasketItem>(basketReturnItem);

                    Basket.Remove(basketReturnItem);
                    Basket.Add(basketItem);
                }
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion
        #region Alter
        [RelayCommand]
        private async void AlterTransactionSelector()
        {
            if(IsBusy || !CanCheckoutAlterOrSave) return;
            IsBusy = true;

            try
            {
                Alterations.Clear();

                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Transaction Alteration (Discounts)");
                foreach(var discount in await RepositoryWrapper.DiscountRepository.GetAll())
                {
                    Alterations.Add(discount);
                }

                string action = await App.Current.MainPage.DisplayActionSheet("Alteration:", "Cancel", null, Alterations.Select(a=>a.Name).ToArray());

                if (action == "Cancel") return;

                var alteration = Alterations.Where(a=>a.Name == action).FirstOrDefault();
                var items = new List<BasketItem>();

                foreach (var item in Basket.Where(br => br is BasketItem).Cast<BasketItem>().ToList())
                {
                    for (var i = 0; i < item.Quantity; i++)
                    {
                        items.Add((BasketItem)item.Clone());
                    }
                }
                var popup = ServiceHelper.GetService<AlterationPage>();
                popup.SetData(alteration, items);
                var adjustmentsResponse = await ServiceHelper.GetService<TillPage>().ShowPopupAsync(popup) as PopupReturnValue<List<BasketItem>>;

                if (adjustmentsResponse.PopupReturnStatus == PopupReturnStatus.Completed)
                {
                    foreach(var adjustment in adjustmentsResponse.ReturnValue)
                    {
                        Basket.Add(adjustment);
                    }
                }
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion
        #region Stored Transactions
        [RelayCommand]
        private async void RetrieveTransaction()
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                SavedTransaction savedTransaction;
                if (StoredTransactions.Count > 1)
                {
                    var baskets = new string[StoredTransactions.Count];
                    for (int i = 0; i < StoredTransactions.Count; i++)
                        baskets[i] = StoredTransactions.ElementAt(i).Name;

                    var action = await App.Current.MainPage.DisplayActionSheet(Strings.Baskets, Strings.Cancel, null, baskets);
                    if (action == Strings.Cancel)
                    {
                        Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Transaction Retrieved", new Dictionary<string, string> { { "Canceled", "True" } });
                        return;
                    }

                    savedTransaction = StoredTransactions.First(sT => sT.Name.Equals(action));
                }
                else
                    savedTransaction = StoredTransactions.First();

                if (Basket.Count > 0)
                    if (!await App.Current.MainPage.DisplayAlert(Strings.Hmm, Strings.BasketWillBeClearedMesg, Strings.OK, Strings.Cancel))
                        return;

                StoredTransactions.Remove(savedTransaction);
                var storedTransactionData = savedTransaction.Data;
                var basket = JsonConvert.DeserializeObject<IBasketRecord[]>(storedTransactionData, new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto
                });

                if (!await RepositoryWrapper.SavedTransactionRepository.Delete(savedTransaction))
                {
                    await App.Current.MainPage.DisplayAlert(Strings.Hmm, Strings.CriticalIssue, Strings.OK);
                    return;
                }

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

        [RelayCommand]
        private async void StoreTransaction()
        {
            if (IsBusy || !CanCheckoutAlterOrSave) return;

            IsBusy = true;
            try
            {
                string transName;
                var firstRun = true;

                do
                {
                    if (!firstRun)
                        await App.Current.MainPage.DisplayAlert(Strings.Hmm, Strings.NameMustBeUniqueMesg, Strings.OK);
                    transName = await App.Current.MainPage.DisplayPromptAsync(Strings.TransactionName, "", Strings.OK, Strings.Cancel, Strings.Name, keyboard: Keyboard.Text);
                    if (string.IsNullOrEmpty(transName) || transName == "Cancel")
                    {
                        Logger.LogEvent(AppLogLevel.Info, $"{GetType().Name}: Transaction Store (Saving)", new Dictionary<string, string> { { "Canceled", "True" } });
                        return;
                    }
                    firstRun = false;
                } while (StoredTransactions.Any(sT => sT.Name.Equals(transName)));

                var basketRecords = new IBasketRecord[Basket.Count()];
                Basket.CopyTo(basketRecords, 0);
                var storeTransaction = new SavedTransaction
                {
                    Id = Guid.NewGuid(),
                    Name = transName,
                    Data = JsonConvert.SerializeObject(
                        basketRecords,
                        Formatting.Indented,
                        new JsonSerializerSettings
                        {
                            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                            TypeNameHandling = TypeNameHandling.Auto
                        })
                };

                if(await RepositoryWrapper.SavedTransactionRepository.Create(storeTransaction))
                {
                    var storedTransFound = await RepositoryWrapper.SavedTransactionRepository.GetAll();
                    StoredTransactions.Clear();
                    foreach(var trans in storedTransFound)
                    {
                        StoredTransactions.Add(trans);
                    }
                }
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion
        #region Checkout
        [RelayCommand]
        private void CancelTransaction()
        {
            Basket.Clear();
        }

        [RelayCommand]
        private async void CheckoutTransaction()
        {
            if (IsBusy || !CanCheckoutAlterOrSave) return;

            IsBusy = true;
            try
            {
                Employee currentEmployee;
                if(AppState.LoggedInEmployees.Count() > 1)
                {
                    var employeeName = await App.Current.MainPage.DisplayActionSheet(Strings.Hmm, null, null, AppState.LoggedInEmployees.Select(e => e.FullName).ToArray());
                    currentEmployee = AppState.LoggedInEmployees.First(e => e.FullName.Equals(employeeName));
                }
                else
                {
                    currentEmployee = AppState.LoggedInEmployees.First();
                }

                var sale = new Sale
                {
                    DateOfSale = DateTime.Now,
                    Total = 0.0m,
                    EmployeeId = currentEmployee.Id,
                    PaySales = new List<PaymentMethod_Sale>(),
                    Notes = new List<Note>(),
                    TillId = AppState.Till.Id,
                    StoreId = AppState.Store.Id
                };

                var change = 0.0m;
                var refundOnly = !Basket.Any(bR => bR is BasketItem and not BasketReturnItem);
                var payMeths = (await RepositoryWrapper.PaymentMethodRepository.GetAllQueryable())
                                                                        .OrderBy(p => p.Name)
                                                                        .ToDictionary<PaymentMethod, string, Func<PaymentMethod>>(payMeth => payMeth.Name, payMeth => () => payMeth);

                sale.Total = Basket.Sum(bR => bR.Price * (bR is BasketReturnItem ? -1 : 1) * bR.Quantity);
                sale.TotalExTax = Basket.Sum(bR => bR.PriceExTax * (bR is BasketReturnItem ? -1 : 1) * bR.Quantity);

                var chosenPayMeths = new List<PaymentMethod>();
                for (var paid = 0.0m; paid != sale.Total;)
                {
                    if (!Basket.Any(bR => bR is BasketReturnItem))
                        if (paid > sale.Total)
                            break;

                    var payMethNames = payMeths.Keys.ToArray();
                    var payMeth = await App.Current.MainPage.DisplayActionSheet(Strings.PayMeth, Strings.Cancel, null, payMethNames);
                    if (payMeth == Strings.Cancel)
                    {
                        Logger.LogEvent(AppLogLevel.Info, "Sale Processing", new Dictionary<string, string> { { "Canceled", "True" } });
                        return;
                    }

                    var pay = new PaymentMethod_Sale();
                    var chosenPayMeth = payMeths[payMeth]();
                    if (chosenPayMeth.MinimumCharge > sale.Total || !refundOnly)
                    {
                        if (chosenPayMeth.Charge != 0.0m)
                        {
                            var note = await RepositoryWrapper.NoteRepository.FindFirstByCondition(nb => nb.Text.Equals(string.Format(Strings.CardChargeNote, chosenPayMeth.Charge))) ??
                                       new Note(string.Format(Strings.CardChargeNote, chosenPayMeth.Charge));
                            Basket.Add(new BasketNote(note, chosenPayMeth.Charge, chosenPayMeth.Charge));
                            sale.Total = Basket.Sum(bR => bR.Price * (bR is BasketReturnItem ? -1 : 1) * bR.Quantity);
                            sale.TotalExTax = Basket.Sum(bR => bR.PriceExTax * (bR is BasketReturnItem ? -1 : 1) * bR.Quantity);
                        }
                    }

                    var moniesInputPage = ServiceHelper.GetService<MoniesInputPage>();
                    moniesInputPage.SetData(chosenPayMeth, sale.Total);
                    var moniesInputReturn = await ServiceHelper.GetService<TillPage>().ShowPopupAsync(moniesInputPage) as PopupReturnValue<decimal>;
                    if (moniesInputReturn.PopupReturnStatus == PopupReturnStatus.Completed)
                    {
                        pay.Amount = moniesInputReturn.ReturnValue;
                        pay.PayId = chosenPayMeth.Id;
                        paid += moniesInputReturn.ReturnValue;
                        if (paid > sale.Total)
                        {
                            if (chosenPayMeth.IsChangeable)
                                change = pay.Change = paid - sale.Total;
                            else
                            {
                                paid -= moniesInputReturn.ReturnValue;
                                continue;
                            }
                        }

                        chosenPayMeths.Add(chosenPayMeth);
                    }

                    sale.PaySales.Add(pay);
                }

                if (chosenPayMeths.Any(p => p.IsChangeable) && Settings.CashbackEnabled)
                {
                    //Cashback stuff
                }

                foreach (var basketNote in Basket.Where(bR => bR is BasketNote || bR is BasketAlteration).Cast<BasketNote>().ToList())
                {
                    sale.Notes.Add(basketNote.Note);
                }

                var @continue = await App.Current.MainPage.DisplayAlert(Strings.Hmm,
                                                                        string.Format(Strings.TransacContinue,
                                                                                      Math.Round(sale.Total,
                                                                                                 2,
                                                                                                 MidpointRounding.AwayFromZero).ToString("C", CultureInfo.CurrentCulture)),
                                                                        Strings.Yes,
                                                                        Strings.Cancel);
                if (!@continue) return; //possibly need to revert in case of chargeble card

                //Loop through all BasketItems and Refund adding
                sale.Transactions = new List<Transaction>();
                sale.Refunds = new List<Refund>();


                var tranId = 0;
                //Loop through all BasketItems in Basket
                foreach (var item in Basket.Where(bR => bR is BasketItem).Cast<BasketItem>().ToList())
                {
                    var tran = new Transaction() { IdOne=tranId, ItemIdOne = item.Item.IdOne, ItemIdTwo = item.Item.IdTwo, Sale = sale, Amount = item.Quantity, ItemCostExPrice = item.Item.ExPrice, ItemCostPrice = item.Item.Price, Transaction_Discounts = new ObservableCollection<Transaction_Discount>() };
                    if (Basket.Where(bR => bR is BasketAlteration).Cast<BasketAlteration>().Any())
                    {
                        var tempIA = Basket.Where(bR => bR is BasketAlteration && bR is not BasketReturnItem).Cast<BasketAlteration>().FirstOrDefault(bA => bA.ItemsAssocitated.Any(iA => iA.Item.IdOne.Equals(item.Item.IdOne) && iA.Item.IdTwo.Equals(item.Item.IdTwo)));
                        if (tempIA != default)
                        {
                            var tranDisc = new Transaction_Discount { DiscountId = tempIA.Discount.Id };
                            tran.Transaction_Discounts.Add(tranDisc);
                        }
                    }
                    sale.Transactions.Add(tran);
                    if (item.PriceExTax != item.Item.ExPrice || item.Price != item.Item.Price)
                    {
                        var itemPriceChange = new CheckoutItemChange() { ItemIdOne = item.Item.IdOne, ItemIdTwo = item.Item.IdTwo, ExPrice = item.PriceExTax, Price = item.Price, Transaction = tran };
                        tran.CheckoutItemChange = itemPriceChange;
                    }
                    tranId++;
                }

                foreach (var returnItem in Basket.Where(bR => bR is BasketReturnItem).Cast<BasketReturnItem>().ToList())
                {
                    var refund = new Refund() { ItemIdOne = returnItem.Item.IdOne, ItemIdTwo = returnItem.Item.IdTwo, Sale = sale, SaleIdReturned = returnItem.ReturnSaleId, Reason = returnItem.Reason, Amount = returnItem.Quantity };
                    sale.Refunds.Add(refund);
                    if (returnItem.PriceExTax != returnItem.Item.ExPrice || returnItem.Price != returnItem.Item.Price)
                    {
                        var itemPriceChange = new CheckoutItemChange() { ItemIdOne = returnItem.Item.IdOne, ItemIdTwo = returnItem.Item.IdTwo, ExPrice = returnItem.PriceExTax, Price = returnItem.Price, Refund = refund };
                        refund.CheckoutItemChange = itemPriceChange;
                    }
                }

                bool escape = false;

                do
                {
                    //Authorisation checks
                    await FinaliseTransaction(sale, change);
                    escape = true;
                } while (!escape);
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion
        #endregion

        #region Operations
        private async void AddItem(string itemId)
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                var item = await RepositoryWrapper.ItemRepository.FindById(itemId, AppState.Business.Id);
                if(item == null)
                {
                    await App.Current.MainPage.DisplayAlert(Strings.Hmm, Strings.ItemNotFoundMesg, Strings.OK);
                    Logger.LogEvent(AppLogLevel.Warn, $"{this.GetType().Name}: Item Added To Basket (from Request)",
                        new Dictionary<string, string> { { "Success", "False" }, { "Reason", "Item no longer exists" } });
                    return;
                }
                
                var tax = await RepositoryWrapper.TaxRepository.FindById(item.TaxId, AppState.Business.Id);
                if(tax == null)
                {
                    await App.Current.MainPage.DisplayAlert(Strings.Hmm, "Tax not found.", Strings.OK);
                    Logger.LogEvent(AppLogLevel.Warn, $"{this.GetType().Name}: Item Added To Basket (from Request)",
                        new Dictionary<string, string> { { "Success", "False" }, { "Reason", "Tax no longer exists" } });
                    return;
                }

                BasketItem tempItem;

                if(SelectedBasketRecord != null &&
                   (SelectedBasketRecord is BasketItem) &&
                   ((BasketItem)SelectedBasketRecord).Item.IdTwo.Equals(item.IdTwo) &&
                   !(SelectedBasketRecord is BasketReturnItem))
                {
                    tempItem = (BasketItem)SelectedBasketRecord;
                }
                else
                {
                    tempItem = Basket.Where(bR => bR is BasketItem)
                                     .Cast<BasketItem>()
                                     .LastOrDefault(bI => bI.Item.IdTwo.Equals(item.IdTwo) &&
                                                          bI.Price.Equals(item.Price) &&
                                                          bI.PriceExTax.Equals(item.ExPrice) &&
                                                          !(bI is BasketReturnItem));
                }

                if (tempItem == default)
                    _basket.Add(new BasketItem(item, (int)Quantity));
                else
                    tempItem.IncrementQuantity((int)Quantity);

                ItemId = String.Empty;
                Quantity = 1;

                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Added To Basket");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task FinaliseTransaction(Sale sale, decimal change)
        {
            if (!await RepositoryWrapper.SaleRepository.SaleTransactionsCreate(sale))
            {
                await App.Current.MainPage.DisplayAlert(Strings.Hmm, Strings.DbIssue, Strings.OK);
                return;
            }

            foreach (var tran in sale.Transactions)
            {
                if (await RepositoryWrapper.StockRepository.Exists(tran.ItemIdOne, tran.ItemIdTwo, AppState.Store.Id))
                {
                    var stock = await RepositoryWrapper.StockRepository.FindById(tran.ItemIdOne, tran.ItemIdTwo, AppState.Store.Id);
                    if (stock == default) continue;
                    
                    await RepositoryWrapper.StockRepository.StockUpdateByQuantityChange(stock, tran.Amount);
                }
            }

            var tasks = new Task[3];
            var trackEventsArgs = new Dictionary<string, string>
            {
                {"Canceled", "False" }
            };

            using var posPrinterManager = ServiceHelper.GetService<PosPrinterManager>();
            if(!Settings.AskForReceipt || await App.Current.MainPage.DisplayAlert(Strings.Hmm, Strings.ReceiptRequired, Strings.Yes, Strings.No))
            {
                trackEventsArgs.Add("Receipt Requested", "True");
                tasks[0] = Task.Run(async () =>
                {
                    _ = await posPrinterManager.InitPrinter();
                    await posPrinterManager.SetUpSalePrint(sale, Basket, null/*Send Store*/, null/*Send Business*/);
                    await posPrinterManager.ExecuteOposOrPdfAsync();

                    trackEventsArgs.Add("Receipt Printed Successfully", "True");
                });
            }

            if (Settings.TryCashDrawer | change != default)
            {
                trackEventsArgs.Add("Cash Drawer Open Requested", "True");
                tasks[1] = posPrinterManager.OpenCashDrawer();
                trackEventsArgs.Add("Cash Drawer Opened Sucessfully", "True");
            }

            try
            {
                await Task.WhenAll(tasks.Where(t => t != null));
            }
            catch(POSObjectException posObjectException)
            {
                switch (posObjectException.POSTargetObjectType)
                {
                    case CommonPOSLibrary.Enums.POSTargetObjectType.Printer:
                        trackEventsArgs.Add("Reciept Printed Successfully", "False");
                        await App.Current.MainPage.DisplayAlert(Strings.Hmm, Strings.PrinterFailedButTransactionSucceeded, Strings.OK);
                        break;
                    case CommonPOSLibrary.Enums.POSTargetObjectType.CashDrawer:
                        trackEventsArgs.Add("Cash Drawer Opened Successfully", "False");
                        trackEventsArgs.Add("Cash Drawer Warning Already Silenced", Settings.CashDrawerWarningSilenced.ToString());
                        if(!Settings.CashDrawerWarningSilenced)
                            Settings.CashDrawerWarningSilenced = !await Application.Current.MainPage.DisplayAlert(Strings.Hmm, Strings.CashDrawerErrorWarning, Strings.OK, Strings.Silence);
                        break;
                }
            }
            catch(POSPrinterException posPrinterException)
            {
                if(posPrinterException.POSPrinterExceptionType == CommonPOSLibrary.Enums.POSPrinterExceptionType.PrinterNotClaimed)
                {
                    Microsoft.AppCenter.Crashes.Crashes.TrackError(posPrinterException);
                }
                trackEventsArgs.Add("Reciept Printed Successfully", "False");
                trackEventsArgs.Add("Printer Not Selected", "True");
                await App.Current.MainPage.DisplayAlert(Strings.Hmm, Strings.PrinterNotSelectedButTransactionSucceeded, Strings.OK);
            }

            Basket.Clear();
            await Application.Current.MainPage.DisplayAlert(Strings.Transaction, Strings.TransConfMesg, Strings.OK);

            Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Sale Processing", trackEventsArgs);
            return;
        }

        #endregion

        #region INotifyPropertyChanged
        private void BasketRecordOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(Basket));
            OnPropertyChanged(nameof(SalesExTax));
            OnPropertyChanged(nameof(SalesIncTax));
        }
        #endregion
    }
}
