using CommunityToolkit.Mvvm.Input;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Helpers;
using Plutus.Frontend.ClientUI.Pages.MainTill.Inventory;
using Plutus.Frontend.ClientUI.Resources.I18N_L10N;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Frontend.ClientUI.Services.Loading;
using Plutus.Frontend.ClientUI.Services.Repository.Contracts;

namespace Plutus.Frontend.ClientUI.ViewModels.MainTill.Inventory
{
    public partial class InventoryViewModel : BaseViewModel
    {
        public InventoryViewModel(ILogger logger, IAppState appState, LoadingViewService loadingViewService, IRepositoryWrapper repositoryWrapper) : base(logger, appState, loadingViewService, repositoryWrapper)
        {
            Title = Strings.InventMgmt;
            Icon = "\uf468";
        }

        public void SetupNavigationButtons(StackLayout leftColumn, StackLayout rightColumn)
        {
            var buttons = new List<Tuple<string, string>>
            {
                Tuple.Create(string.Format(Strings.ViewAllArg, Strings.Inventory), "OpenViewAllInventoryCommand"),
                Tuple.Create($"{Strings.Add} {Strings.Inventory} {Strings.Item}", "OpenAddInventoryItemCommand"),
            };

            for(int i = 0; i < buttons.Count; i++)
            { 
                var button = new Button { Text = buttons[i].Item1 };
                button.SetBinding(Button.CommandProperty, buttons[i].Item2);
                if (i % 2 == 0)
                    leftColumn.Children.Add(button);
                else
                    rightColumn.Children.Add(button);
            }
        }

        #region Commands
        [RelayCommand]
        private async void OpenViewAllInventory()
        {
            if (IsBusy) return;

            IsBusy = true;
            //LoadingViewService.ShowLoadingView();

            try
            {
                await App.Current.MainPage.Navigation.PushAsync(ServiceHelper.GetService<ViewAllInventoryPage>());
            }
            finally
            {
                //LoadingViewService.HideLoadingView();
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async void OpenAddInventoryItem()
        {
            if (IsBusy) return;

            IsBusy = true;
            //LoadingViewService.ShowLoadingView();

            try
            {
                await App.Current.MainPage.Navigation.PushAsync(ServiceHelper.GetService<AddEditInventoryItemPage>());
            }
            finally
            {
                //LoadingViewService.HideLoadingView();
                IsBusy = false;
            }
        }
        #endregion
    }
}
