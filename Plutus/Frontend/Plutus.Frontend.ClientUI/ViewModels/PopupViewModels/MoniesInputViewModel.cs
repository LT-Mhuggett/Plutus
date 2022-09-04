using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plutus.Entities.Models;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Core.Enum;
using Plutus.Frontend.ClientUI.Core.EventArgs;
using Plutus.Frontend.ClientUI.Core.Models;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Frontend.ClientUI.Services.Loading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plutus.Frontend.ClientUI.ViewModels.PopupViewModels
{
    public partial class MoniesInputViewModel : PopupBaseViewModel<decimal>
    {
        [ObservableProperty]
        private PaymentMethod _paymentMethod;
        [ObservableProperty]
        private decimal _amountRemaining;
        [ObservableProperty]
        private decimal _amountPaying = 0.0m;

        public MoniesInputViewModel(ILogger logger, IAppState appState, LoadingViewService loadingViewService) : base(logger, appState, loadingViewService)
        {
        }

        [RelayCommand]
        private void MoniesIncrement(object incrementingAmountString)
        {
            if(decimal.TryParse((string)incrementingAmountString, out decimal incrementingAmount))
            {
                _amountPaying += incrementingAmount;
                if (_amountPaying >= _amountRemaining)
                    OnCloseRequest(this, new PopupCloseRequestEventArgs<decimal>(this, new PopupReturnValue<decimal>(PopupReturnStatus.Completed, _amountPaying)));
            }
        }

        [RelayCommand]
        private void PayInFull()
        {
            OnCloseRequest(this, new PopupCloseRequestEventArgs<decimal>(this, new PopupReturnValue<decimal>(PopupReturnStatus.Completed, _amountRemaining)));
        }       

        [RelayCommand]
        private void Cancel()
        {
            OnCloseRequest(this, new PopupCloseRequestEventArgs<decimal>(this, new PopupReturnValue<decimal>(PopupReturnStatus.Canceled, 0m)));
        }
    }
}
