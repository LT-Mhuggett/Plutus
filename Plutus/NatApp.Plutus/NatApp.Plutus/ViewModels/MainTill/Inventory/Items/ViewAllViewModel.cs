using Database.Models;
using Microsoft.EntityFrameworkCore;
using NatApp.Plutus.Helpers.Extensions;
using NatApp.Plutus.Helpers.Security;
using NatApp.Plutus.Helpers.Validators;
using NatApp.Plutus.Views.MainTill.Inventory.Items;
using Syncfusion.DataSource;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace NatApp.Plutus.ViewModels.MainTill.Inventory.Items
{
    public class ViewAllViewModel : BaseViewModel
    {
        #region Private Fields
        private string _searchText;
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
        public DataSource SfListViewDataSource
        {
            get => _sfListViewDataSource;
            set => SetProperty(ref _sfListViewDataSource, value);
        }
        public ObservableCollection<ItemModel> Items { get; private set; } = new ObservableCollection<ItemModel>();
        #endregion
        #endregion

        public ViewAllViewModel()
        {
            Title = "View All Items";
            #region Init
            Device.BeginInvokeOnMainThread(() =>
            {
                SfListViewDataSource.GroupDescriptors.Add(new GroupDescriptor()
                {
                    PropertyName = "Name",
                    KeySelector = (obj) =>
                    {
                        if (obj is ItemModel item)
                            return item.Name[0].ToString();
                        return "";
                    }
                });
            });
            #endregion
        }

        public void InitItems()
        {
            App.SetLoading(true);
            Items.Clear();
            Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
            using (var db = new Helpers.Database.Database(databaseProvider))
            {
                _limit = db.Get<ItemModel>().Count();
                db.SetTrackingBehavior(QueryTrackingBehavior.NoTracking);
                var data = db.Get<ItemModel>()
                    .Include(i => i.Stock)
                    .Include(i => i.Vat)
                    .OrderBy(i => i.Name).Take(_limit).ToList();
                Items = new ObservableCollection<ItemModel>(data);
            }
            OnPropertyChanged("Items");
            App.SetLoading(false);
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

        private async void ExecuteItemFilter()
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
        private async void ExecuteAddToBasket(string itemId)
        {
            MessagingCenter.Send(this, "AddToBasket", itemId);
        }

        private async void ExecuteOpenEditItem(string itemId)
        {
            await App.Current.MainPage.Navigation.PushAsync(new AddEditView(itemId));
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

                        var viewElements = new Tuple<string, string, IEnumerable<IValidator>, bool, bool>[]
                        {
                        Tuple.Create(string.Format("IdArg".Translate(), "Item".Translate()), itemId, idValidtors.AsEnumerable(), false, false),
                        Tuple.Create(string.Format("ToIncrement/Decrement".Translate(), "Quantity".Translate()), "", qtyValidators.AsEnumerable(), false, true)
                        };

                        var data = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(viewElements, "Confirm".Translate(), false, "UpdateStock".Translate(), "Cancel".Translate());

                        if (data.Any(d => d as string == null || string.IsNullOrEmpty(d as string)))
                            return;

                        using (var db = new Helpers.Database.Database(databaseProvider, empId))
                        {
                            var stock = db.Get<StockModel>().Where(s => s.ItemId.Equals(itemId)).FirstOrDefault();
                            if (stock == default(StockModel))
                            {
                                stock = new StockModel { ItemId = itemId, StoreId = App.GetViewModel().Store.Id };
                                db.Add(stock);
                            }
                            stock.Quantity += int.Parse(data[1].ToString());
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
