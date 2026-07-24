using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Core.Enum;
using Plutus.Frontend.ClientUI.Core.EventArgs;
using Plutus.Frontend.ClientUI.Core.Models;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Frontend.ClientUI.Services.Loading;

namespace Plutus.Frontend.ClientUI.ViewModels.PopupViewModels
{
    public partial class AdjustItemViewModel : PopupBaseViewModel<Tuple<decimal, decimal>>
    {
        #region Fields
        [ObservableProperty]
        private decimal _priceExTax;

        [ObservableProperty]
        private decimal _price;
        #endregion
        public AdjustItemViewModel(ILogger logger, IAppState appState, LoadingViewService loadingViewService) : base(logger, appState, loadingViewService)
        {
        }

        #region Command Can Executes
        #endregion

        #region Commands
        [RelayCommand]
        private void ConfirmPriceAdjust()
        {
            OnCloseRequest(this, new PopupCloseRequestEventArgs<Tuple<decimal, decimal>>(this, new PopupReturnValue<Tuple<decimal, decimal>>(PopupReturnStatus.Completed, new Tuple<decimal, decimal>(Price, PriceExTax))));
        }

        [RelayCommand]
        private void Cancel()
        {
            OnCloseRequest(this, new PopupCloseRequestEventArgs<Tuple<decimal, decimal>>(this, new PopupReturnValue<Tuple<decimal, decimal>>(PopupReturnStatus.Canceled, null)));
        }
        #endregion
    }
}
