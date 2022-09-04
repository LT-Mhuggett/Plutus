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
    public partial class ReturnItemViewModel : PopupBaseViewModel<Tuple<string, string>>
    {
        #region Fields
        [ObservableProperty]
        private string _saleId;

        [ObservableProperty]
        private string _reason;
        #endregion

        public ReturnItemViewModel(ILogger logger, IAppState appState, LoadingViewService loadingViewService) : base(logger, appState, loadingViewService)
        {
        }

        #region Command Can Execute
        private bool CanExecuteConfirmReturnItem() => !(string.IsNullOrEmpty(_saleId) || string.IsNullOrEmpty(_reason));
        #endregion

        #region Commands
        [RelayCommand(CanExecute = nameof(CanExecuteConfirmReturnItem))]
        private void ConfirmReturnItem()
        {
            OnCloseRequest(this, new PopupCloseRequestEventArgs<Tuple<string, string>>(this, new PopupReturnValue<Tuple<string, string>>(PopupReturnStatus.Completed, new Tuple<string, string>(SaleId, Reason))));
        }

        [RelayCommand]
        private void Cancel()
        {
            OnCloseRequest(this, new PopupCloseRequestEventArgs<Tuple<string, string>>(this, new PopupReturnValue<Tuple<string, string>>(PopupReturnStatus.Canceled, null)));
        }
        #endregion
    }
}
