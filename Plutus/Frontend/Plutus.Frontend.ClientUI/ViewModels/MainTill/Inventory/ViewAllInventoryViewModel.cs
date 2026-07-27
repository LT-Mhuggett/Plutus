using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Maui.Dispatching;
using Plutus.Entities.Models;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Core.Messages;
using Plutus.Frontend.ClientUI.Helpers;
using Plutus.Frontend.ClientUI.Pages.MainTill.Inventory;
using Plutus.Frontend.ClientUI.Resources.I18N_L10N;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Frontend.ClientUI.Services.Loading;
using Plutus.Frontend.ClientUI.Services.Repository.Contracts;
using System.Collections.ObjectModel;

namespace Plutus.Frontend.ClientUI.ViewModels.MainTill.Inventory
{
    public partial class ViewAllInventoryViewModel : BaseViewModel
    {
        #region Fields
        [ObservableProperty]
        private string _searchText;
        private int _limit;
        [ObservableProperty]
        private bool _isRefreshing;
        #endregion

        #region Properties
        public ObservableCollection<Item> Items { get; private set; }
        #endregion

        public ViewAllInventoryViewModel(ILogger logger, IAppState appState, IDispatcher dispatcher, LoadingViewService loadingViewService, IRepositoryWrapper repositoryWrapper) : base(logger, appState, loadingViewService, repositoryWrapper)
        {
            Title = Strings.Inventory;
            Icon = "&#f03a";
            Items = new ObservableCollection<Item>();
        }

        #region Commands
        [RelayCommand]
        private async Task TestStockChange()
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                await RepositoryWrapper.StockRepository.StockUpdateByQuantityChange(Items.Last().Stock, -4);
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private void AddToBasket(object itemParameter)
        {
            if (IsBusy) return;

            //LoadingViewService.ShowLoadingView();
            try
            {
                if (itemParameter is Item item)
                {
                    WeakReferenceMessenger.Default.Send(new AddToBasketMessage(item.IdOne));
                    Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Added To Basket Requested (from ViewAllViewModel)");
                }
            }
            finally
            {
                //LoadingViewService.HideLoadingView();
            }
        }

        [RelayCommand]
        private async void OpenEditItem(object itemParameter)
        {
            if (IsBusy) return;

            IsBusy = true;
            //LoadingViewService.ShowLoadingView();
            try
            {
                if (itemParameter is Item item)
                {
                    var editItemPage = ServiceHelper.GetService<AddEditInventoryItemPage>();
                    await App.Current.MainPage.Navigation.PushAsync(editItemPage);
                    await editItemPage.ItemToEditId(item.IdOne);
                    Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Edit Opened (from ViewAllViewModel)");
                }
            }
            finally
            {
                IsBusy = false;
                //LoadingViewService.HideLoadingView();
            }
        }

        [RelayCommand]
        private async void OpenAddItem()
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                await App.Current.MainPage.Navigation.PushAsync(ServiceHelper.GetService<AddEditInventoryItemPage>());
                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Add Opened (from ViewAllViewModel");
            }finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async void UpdateItemStock(object itemParameter)
        {
            if (IsBusy) return;

            IsBusy = true;
            //LoadingViewService.ShowLoadingView();
            try
            {
                if(itemParameter is Item item)
                {

                }
                //Perform Stock Update
            }
            finally
            {
                IsBusy = false;
                //LoadingViewService.HideLoadingView();
            }
        }


        [RelayCommand]
        private void ExecuteItemFilter()
        {

        }
        #endregion


        public async void InitItems()
        {
            IsRefreshing = true;
            //LoadingViewService.ShowLoadingView();
            try
            {
                _limit = (await RepositoryWrapper.ItemRepository.GetAll(AppState.Business.Id)).Count();
                Items.Clear();
                var data = (await RepositoryWrapper.ItemRepository.GetAllQueryable(AppState.Business.Id))
                    .Include(i => i.Stock)
                    .Include(i => i.Tax)
                    .OrderBy(i => i.Name).Take(_limit).ToList();
                Items = new ObservableCollection<Item>(data);
                OnPropertyChanged(nameof(Items));
            }
            finally
            {
                IsRefreshing = false;
                //LoadingViewService.HideLoadingView();
            }
        }
    }
}
