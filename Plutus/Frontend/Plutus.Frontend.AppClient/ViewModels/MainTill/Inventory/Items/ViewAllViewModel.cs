using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CustomViews.Structs;
using Microsoft.Maui.ApplicationModel;
using Plugin.Maui.MessagingCenter;
using Database.Models;
using Microsoft.EntityFrameworkCore;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Helpers.Security;
using Plutus.Frontend.AppClient.Helpers.Validators;
using Plutus.Frontend.AppClient.Services.Analytics;
using Plutus.Frontend.AppClient.Views.MainTill.Inventory.Items;
using Syncfusion.Maui.ListView;
using Syncfusion.Maui.DataSource;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.ViewModels.MainTill.Inventory.Items
{
    public class ViewAllViewModel : BaseViewModel
    {
        #region Private Fields
        private string _searchText;
        // ⚠ Left NULL until the view assigns it — see the property. It is the list's own
        // DataSource, pushed in by a OneWayToSource binding.
        private DataSource _sfListViewDataSource;
        private int _limit;
        #endregion

        #region Properties
        #region Public
        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value, onChanged: () => ExecuteItemFilter());
        }
        /// <summary>
        /// ⚠ THE VIEW PUSHES THIS IN — the XAML binds `Mode=OneWayToSource`, so the list view
        /// assigns its OWN `DataSource` here. The viewmodel must never manufacture one: a
        /// locally-created DataSource is an orphan the list never reads, so any grouping applied to
        /// it silently does nothing.
        ///
        /// ⚠ Which is why the grouping is applied HERE, on assignment, rather than in the
        /// constructor. The constructor ran before the binding had pushed anything and dereferenced
        /// null — that is the NullReferenceException that made "View all items" crash every time it
        /// was opened, wrapped in a TargetInvocationException from the XAML loader so the stack
        /// pointed at InitializeComponent.
        /// </summary>
        public DataSource SfListViewDataSource
        {
            get => _sfListViewDataSource;
            set
            {
                SetProperty(ref _sfListViewDataSource, value);
                ApplyGrouping();
            }
        }

        /// <summary>Group by first letter. ⚠ Null-safe: the old selector did `item.Name[0]`, which
        /// throws on an item with no name — inside the list's own layout pass, where it strands the
        /// screen rather than surfacing.</summary>
        private void ApplyGrouping()
        {
            var source = _sfListViewDataSource;
            if (source is null || source.GroupDescriptors.Count > 0) return;

            source.GroupDescriptors.Add(new GroupDescriptor
            {
                PropertyName = "Name",
                KeySelector = obj =>
                    obj is ItemModel item && !string.IsNullOrWhiteSpace(item.Name)
                        ? item.Name.Trim()[0].ToString().ToUpperInvariant()
                        : "#",
            });
        }
        public ObservableCollection<ItemModel> Items { get; private set; } = new ObservableCollection<ItemModel>();
        #endregion
        #endregion

        public ViewAllViewModel()
        {
            Title = "View All Items";

            // ⚠ NO GROUPING SET UP HERE. It used to be done from the constructor inside
            // `BeginInvokeOnMainThread`, against a DataSource the VIEW had not pushed in yet — a
            // guaranteed NullReferenceException, and the reason this screen crashed on every open.
            // Grouping is applied when the view assigns `SfListViewDataSource`.
        }

        /// <summary>
        /// Fill the browsable item list from the V2 CATALOGUE.
        ///
        /// ⚠ THIS READ THE LEGACY `Items` TABLE AND SILENTLY SHOWED NOTHING. On a
        /// portal-provisioned till that table is empty and stays empty — the catalogue arrives
        /// through `/api/v1/catalogue/changes` into the v2 store — so browse-and-tap, one of the two
        /// ways an operator puts something in a basket, simply did not exist. No error, no empty
        /// state, just a blank list, which reads as "this shop sells nothing".
        ///
        /// ⚠ Loads OFF the UI thread. The old version opened SQLite and ran a two-`Include` query
        /// synchronously with the screen already up.
        /// </summary>
        public void InitItems()
        {
            App.SetLoading(true);

            _ = Task.Run(async () =>
            {
                var loaded = new List<ItemModel>();
                try
                {
                    // ⚠ TIMED OUT, because the store is SHARED. `TillStoreAccess` serialises every
                    // caller behind one semaphore, so a read that never returns does not just hang
                    // this screen — it blocks the heartbeat, the outbox drain and the catalogue
                    // sync behind it, and the till goes quiet with no error anywhere.
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    var catalogue = await Services.Storage.TillStoreAccess.UseAsync(
                        s => s.BrowseAsync(500, timeout.Token), timeout.Token);

                    // ⚠ Mapped to the legacy `ItemModel` because that is what the list view binds
                    // to, and MAUI bindings fail SILENTLY — swapping the bound type would blank the
                    // rows rather than fail. The model goes when the inventory screen is reshaped.
                    loaded = catalogue.Select(c => new ItemModel
                    {
                        Id = c.IdOne,
                        Name = c.Name,
                        Price = c.PricePence / 100m,
                        // ⚠ Derived from the rate, NOT read off the item: the v2 catalogue stores
                        // the inc price and a rate, and this list is a browse — the SALE price pair
                        // is resolved at basket-add by `EffectivePricePairAsync`, which is the only
                        // thing allowed to decide what a line costs.
                        ExPrice = c.VatRateBp > 0
                            ? Math.Round(c.PricePence / (1m + c.VatRateBp / 10000m)) / 100m
                            : c.PricePence / 100m,
                    }).ToList();
                }
                catch (Exception ex)
                {
                    Services.Analytics.CrashLog.Write("ViewAllViewModel.InitItems", ex);
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    // ⚠ THE OVERLAY COMES DOWN FIRST, and this ordering is the fix for a HANG.
                    // Setting the collection makes the list group and lay out synchronously — and
                    // anything that throws in there (a null item name in the group selector was the
                    // real one) killed this lambda before `SetLoading(false)` ran, leaving a
                    // spinner over a dead screen with nothing in any log. Clearing it first means
                    // the worst case is a visibly empty list, which is diagnosable.
                    //
                    // ⚠ And in its OWN try, because `SetLoading` reaches through
                    // `App.GetViewModel()`, which casts `_app.BindingContext` — it can throw, and if
                    // it took the binding down with it the screen would be blank AND covered.
                    try { App.SetLoading(false); }
                    catch (Exception ex) { Services.Analytics.CrashLog.Write("ViewAllViewModel.Overlay", ex); }

                    try
                    {
                        Items = new ObservableCollection<ItemModel>(loaded);
                        OnPropertyChanged(nameof(Items));
                    }
                    catch (Exception ex)
                    {
                        Services.Analytics.CrashLog.Write("ViewAllViewModel.Bind", ex);
                    }
                });
            });
        }

        #region Commands
        /*
        Command _searchItemsCommand;

        public Command SearchItemsCommand
        {
            get => _searchItemsCommand ?? (_searchItemsCommand = new Command(ExecuteItemSearch));
        }*/
        /*
        #region Load More Items
        
        Command _loadMoreItemsCommand;

        public Command LoadMoreItemsCommand
        {
            get => _loadMoreItemsCommand ?? (_loadMoreItemsCommand = new Command(ExecuteItemLoad, CanLoadMore));
        }

        private bool CanLoadMore()
        {
            Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
            using (var db = new Helpers.Database.Database(databaseProvider))
            {
                return db.Get<ItemModel>().Count() > Items.Count;
            }
        }
        #endregion
        */

        Command _addToBasketCommandArg;
        public Command AddToBasketCommandArg
        {
            get => _addToBasketCommandArg ?? (_addToBasketCommandArg = new Command<string>(ExecuteAddToBasket));
        }

        Command _openEditItemCommandArg;
        public Command OpenEditItemCommandArg
        {
            get => _openEditItemCommandArg ?? (_openEditItemCommandArg = new Command<string>(ExecuteOpenEditItem));
        }

        Command _updateItemStockCommandArg;
        public Command UpdateItemStockCommandArg
        {
            get => _updateItemStockCommandArg ?? (_updateItemStockCommandArg = new Command<string>(ExecuteUpdateItemStock));
        }

        #endregion

        #region Execute Command
        #region Search & Filter
        /*
        private async void ExecuteItemSearch()
        {
            Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
            using (var db = new Helpers.Database.Database(databaseProvider))
            {
                db.SetTrackingBehavior(QueryTrackingBehavior.NoTracking);
                Items.AddRange(db.Get<ItemModel>()
                    .Include(i => i.Stock)
                    .Include(i => i.Vat)
                    .OrderBy(i => i.Name)
                    .Where(i => i.Name.Contains(
                        SearchText,
                        StringComparison.OrdinalIgnoreCase) ||
                        (i.Brand != null ? i.Brand.Contains(
                            SearchText,
                            StringComparison.OrdinalIgnoreCase) : false) ||
                        (i.Desc != null ? i.Desc.Contains(
                            SearchText,
                            StringComparison.OrdinalIgnoreCase) : false))
                    .Where(item => !Items.Contains(item))
                    .Select(item => item));
            }
        }*/

        private void ExecuteItemFilter()
        {
            if (SfListViewDataSource != null)
            {
                SfListViewDataSource.Filter = FilterItems;
                SfListViewDataSource.RefreshFilter();
            }
        }
        /*
        private async void ExecuteItemLoad()
        {
            LoadItems();
        }*/
        #endregion
        private void ExecuteAddToBasket(string itemId)
        {
            MessagingCenter.Send(this, "AddToBasket", itemId);
            Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Added To Basket Requested (from ViewAllViewModel)");
        }

        private async void ExecuteOpenEditItem(string itemId)
        {
            await App.Current.MainPage.Navigation.PushAsync(new AddEditView(itemId));
            Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Edit Opened (from ViewAllViewModel)");
        }

        private async void ExecuteUpdateItemStock(string itemId)
        {
            if (IsBusy)
                return;

            IsBusy = true;
            try
            {
                var empId = App.GetViewModel().EmployeeId;
                bool escape = false;
                do
                {
                    Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
                    if (empId.IsAuthorised("Item", Database.Enums.Permissions.Write, databaseProvider))
                    {
                        var idValidtors = new IValidator[]
                        {
                        new RequiredValidator()
                        };

                        var qtyValidators = new IValidator[]
                        {
                        new RequiredValidator(),
                        new IntegerValidator()
                        };

                        var viewElements = new ViewElementData[]
                        {
                            new ViewElementData(1, string.Format("IdArg".Translate(), "Item".Translate()), itemId, idValidtors.AsEnumerable(), false, false),
                            new ViewElementData(2, string.Format("ToIncrement/Decrement".Translate(), "Quantity".Translate()), "", qtyValidators.AsEnumerable(), false, true)
                        };

                        var data = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(viewElements, "Confirm".Translate(), false, "UpdateStock".Translate(), "Cancel".Translate());

                        if (data.Any(d => string.IsNullOrEmpty(d.Value)))
                            return;

                        using (var db = new Helpers.Database.Database(databaseProvider, empId))
                        {
                            var stock = db.Get<StockModel>().Where(s => s.ItemId.Equals(itemId)).FirstOrDefault();
                            if (stock == default(StockModel))
                            {
                                stock = new StockModel { ItemId = itemId, StoreId = App.GetViewModel().Store.Id };
                                db.Add(stock);
                            }
                            data.TryGetValue(2, out var quatityText);
                            stock.Quantity += int.Parse(quatityText);
                            if (!db.Save())
                                Debug.Write("Database save issue!");
                        }
                        InitItems();
                        return;
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
        #endregion

        #region Operations
        private bool FilterItems(object obj)
        {
            if (string.IsNullOrEmpty(SearchText))
                return true;

            return obj is ItemModel item &&
                   (item.Id.Contains(
                        SearchText, StringComparison.OrdinalIgnoreCase) ||
                        item.Name.Contains(
                        SearchText,
                        StringComparison.OrdinalIgnoreCase) ||
                        (item.Brand?.Contains(
                        SearchText,
                        StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (item.Desc?.Contains(
                        SearchText,
                        StringComparison.OrdinalIgnoreCase) ?? false));
        }
        /*
        private void LoadItems()
        {
            Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
            using (var db = new Helpers.Database.Database(databaseProvider))
            {
                db.SetTrackingBehavior(QueryTrackingBehavior.NoTracking);
                foreach (var item in db.Get<ItemModel>()
                    .Include(i => i.Stock)
                    .Include(i => i.Vat)
                    .OrderBy(i => i.Name)
                    .Skip(Items.Count)
                    .Where(item => !Items.Contains(item))
                    .Take(_limit)
                    .Select(item => item))
                    Items.Add(item);
            }
        }*/
        #endregion

        #region Disposeable Implementation
        protected override void Dispose(bool disposing)
        {
            Items = null;
            _searchText = null;
            _sfListViewDataSource = null;
            base.Dispose(disposing);
        }
        #endregion
    }
}
