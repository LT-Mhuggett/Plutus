using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plutus.Entities.Models;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Core.Models;
using Plutus.Frontend.ClientUI.Helpers;
using Plutus.Frontend.ClientUI.Pages.PopupViews;
using Plutus.Frontend.ClientUI.Resources.I18N_L10N;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Frontend.ClientUI.Services.Loading;
using Plutus.Frontend.ClientUI.Services.Repository.Contracts;
using System.Collections.ObjectModel;

namespace Plutus.Frontend.ClientUI.ViewModels.MainTill.Inventory
{
    public partial class AddEditInventoryItemViewModel : BaseViewModel
    {
        #region Fields
        private bool _categoriesTaxesLoaded = false;
        internal Page page;
        [ObservableProperty]
        private Item _item;
        [ObservableProperty]
        private bool _isUpdate;
        [ObservableProperty]
        private Tax _tax;
        [ObservableProperty]
        private int _stock;
        [ObservableProperty]
        private Category _category;
        [ObservableProperty]
        private string _createUpdateText;
        #endregion

        #region Properties
        public ObservableCollection<Tax> Taxes { get; set; }
        public ObservableCollection<Category> Categories {get; set; }
        #endregion

        public AddEditInventoryItemViewModel(ILogger logger, IAppState appState, LoadingViewService loadingViewService, IRepositoryWrapper repositoryWrapper) : base(logger, appState, loadingViewService, repositoryWrapper)
        {
            Title = Strings.Add;
            CreateUpdateText = Strings.Create;
            IsUpdate = false;
            Item = new Item();
            Taxes = new();
            Categories = new();

            App.Current.MainPage.Dispatcher.Dispatch(async () =>
            {
                foreach (var cat in await RepositoryWrapper.CategoryRepository.FindAllByCondition(new Repository.QueryParameters.CategoryParameters(), AppState.Business.Id))
                    Categories.Add(cat);
                foreach (var tax in await RepositoryWrapper.TaxRepository.FindAllByCondition(new Repository.QueryParameters.TaxParameters(), AppState.Business.Id))
                    Taxes.Add(tax);

                Categories.Add(new Category
                {
                    IdOne = Guid.Empty,
                    Name = string.Format(Strings.CreateWithArg, Strings.Category)
                });
                _categoriesTaxesLoaded = true;
            });

            Categories.CollectionChanged += (sender, e) =>
            {
                OnPropertyChanged(nameof(Categories));
            };
            Taxes.CollectionChanged += (sender, e) =>
            {       
                OnPropertyChanged(nameof(Taxes));
            };
        }

        #region Commands
        [RelayCommand]
        private async void CreateUpdate()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (IsUpdate)
                    await UpdateItem();
                else
                {
                    await CreateItem();
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async void CreateCategory()
        {
            if (IsBusy || Category == default || Category.IdOne != Categories.Last().IdOne) return;
            IsBusy = true;

            try
            {
                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Category Create (from AddEditViewModel)");
                var popup = ServiceHelper.GetService<CategoryCreatePage>();
                await page.ShowPopupAsync(popup);
                var categoryCreateResponse = popup.Result;
                //Countermesaure code till CommunityToolkit/Maui#568 is merged to release branch
#if WINDOWS
                // Popup v2 made Popup a plain ContentView, so PlatformView is now the
                // ContentPanel itself - no more MauiPopup wrapper/.Target indirection.
                if (popup.Handler?.PlatformView is Microsoft.Maui.Platform.ContentPanel panel)
                    panel.ContextFlyout = null;
#endif
                if (categoryCreateResponse.PopupReturnStatus == Core.Enum.PopupReturnStatus.Completed)
                {
                    Categories.Clear();
                    foreach (var cat in await RepositoryWrapper.CategoryRepository.FindAllByCondition(new Repository.QueryParameters.CategoryParameters(), AppState.Business.Id))
                        Categories.Add(cat);
                    App.Current.MainPage.Dispatcher.Dispatch(() =>
                    {
                        Categories.Add(new Category
                        {
                            IdOne = Guid.Empty,
                            Name = string.Format(Strings.CreateWithArg, Strings.Category)
                        });
                        Category = Categories.First(c => c.IdOne.Equals(categoryCreateResponse.ReturnValue.IdOne));
                    });
                }
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion

        #region Operations
        private async Task CreateItem()
        {
            Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item Created (from AddEditViewModel)");
            if(await RepositoryWrapper.ItemRepository.FindById(Item.IdOne, AppState.Business.Id) != default)
            {
                await App.Current.MainPage.DisplayAlert(Strings.Hmm, Strings.ItemExists, Strings.OK);
                return;
            }
            
            Item.IdTwo = AppState.Business.Id;
            Item.TaxId = Tax.IdOne;
            Item.CatId = Category.IdOne;

            if (!await RepositoryWrapper.ItemRepository.Create(Item))
            {
                Logger.LogEvent(AppLogLevel.Error, $"{this.GetType().Name}: Item create failed.");
                await App.Current.MainPage.DisplayAlert(Strings.Hmm, Strings.DbIssue, Strings.OK);
                return;
            }

            CreateUpdateStock();

            if (Item.Stock != default)
            {
                if (!await RepositoryWrapper.StockRepository.Create(Item.Stock))
                {
                    Logger.LogEvent(AppLogLevel.Error, $"{this.GetType().Name}: Stock create failed.");
                    await App.Current.MainPage.DisplayAlert(Strings.Hmm, Strings.DbIssue, Strings.OK);
                    return;
                }
            }

            await App.Current.MainPage.DisplayAlert(Strings.Success, Strings.Saved, Strings.OK);
            await App.Current.MainPage.Navigation.PopAsync();
            return;
        }

        private async Task UpdateItem()
        {
            Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Item update (from AddEditViewModel)");
            CreateUpdateStock();
            var tempItem = await RepositoryWrapper.ItemRepository.FindById(Item.IdOne, AppState.Business.Id);
            if(tempItem == default)
            {
                await App.Current.MainPage.DisplayAlert(Strings.Hmm, Strings.ItemNotFoundMesg, Strings.OK);
                return;
            }

            Item.Tax = null;
            Item.Cat = null;
            Item.TaxId = Tax.IdOne;
            Item.CatId = Category.IdOne;

            if(await RepositoryWrapper.ItemRepository.Update(Item))
            {
                Logger.LogEvent(AppLogLevel.Error, $"{this.GetType().Name}: Item update failed.");
                await App.Current.MainPage.DisplayAlert(Strings.Hmm, Strings.DbIssue, Strings.OK);
                return;
            }

            await App.Current.MainPage.DisplayAlert(Strings.Success, Strings.Saved, Strings.OK);
            await App.Current.MainPage.Navigation.PopAsync();
            return;
        }

        private void CreateUpdateStock()
        {
            if (Stock < 0)
                Item.Stock = null;
            else if(Item.Stock == default)
            {
                Item.Stock = new Stock
                {
                    Quantity = Stock,
                    IdOne = Item.IdOne,
                    IdTwo = AppState.Business.Id,
                    IdThree = AppState.Store.Id
                };
            }
            else
            {
                Item.Stock.Quantity = Stock;
            }
        }

        public async Task ItemToEditId(string itemId)
        {
            var item = await RepositoryWrapper.ItemRepository.FindById(itemId, AppState.Business.Id);
            if (item == default)
            {
                await App.Current.MainPage.DisplayAlert(Strings.Hmm, Strings.ItemNotFoundMesg, Strings.OK);
                await App.Current.MainPage.Navigation.PopAsync();
                return;
            }
            Item = item;

            var stock = await RepositoryWrapper.StockRepository.FindById(itemId, AppState.Business.Id, AppState.Store.Id);
            if (stock == default)
                Stock = -1;
            else
                Stock = stock.Quantity;


            while (!_categoriesTaxesLoaded)
            {
                await Task.Delay(100);
            }

            Category = Categories.FirstOrDefault(c => c.IdOne.Equals(item.CatId));
            Tax = Taxes.FirstOrDefault(t => t.IdOne.Equals(item.TaxId));

            if (Category == default)
                await App.Current.MainPage.DisplayAlert(Strings.Hmm, Strings.CategoryMissing, Strings.OK);
            if (Tax == default)
                await App.Current.MainPage.DisplayAlert(Strings.Hmm, Strings.TaxMissing, Strings.OK);
        }
        #endregion
    }
}
