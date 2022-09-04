using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
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
    public partial class AuthorisationViewModel : PopupBaseViewModel<Employee>
    {
        #region fields
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsPasswordRequired))]
        private string _email;

        [ObservableProperty]
        private string _password;
        #endregion

        #region Properties
        public bool IsPasswordRequired => !AppState.LoggedInEmployees.Any(e => e.Email.ToLower().Equals(Email));
        #endregion

        public AuthorisationViewModel(ILogger logger, IAppState appState, LoadingViewService loadingViewService) : base(logger, appState, loadingViewService)
        {
        }

        #region Command Can Executes
        private bool CanAuthorise() => string.IsNullOrEmpty(_email) && (string.IsNullOrEmpty(_password) || !IsPasswordRequired);
        #endregion

        #region Commands
        [RelayCommand(CanExecute=nameof(CanAuthorise))]
        private void Authorise()
        {
            OnCloseRequest(this, new PopupCloseRequestEventArgs<Employee>(this, new PopupReturnValue<Employee>(PopupReturnStatus.Completed, null)));
        }

        [RelayCommand]
        private void Cancel()
        {
            OnCloseRequest(this, new PopupCloseRequestEventArgs<Employee>(this, new PopupReturnValue<Employee>(PopupReturnStatus.Canceled, null)));
        }
        #endregion
    }
}
