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
        // null-guarded (BugFix plan, Bug 1 latent): employee emails can be null.
        public bool IsPasswordRequired => !AppState.LoggedInEmployees.Any(e => (e.Email ?? string.Empty).ToLower().Equals(Email?.ToLower()));
        #endregion

        public AuthorisationViewModel(ILogger logger, IAppState appState, LoadingViewService loadingViewService) : base(logger, appState, loadingViewService)
        {
        }

        #region Command Can Executes
        // Fixed inverted logic (BugFix plan, Bug 1 latent): previously enabled only when
        // the email was EMPTY.
        private bool CanAuthorise() => !string.IsNullOrEmpty(_email) && (!IsPasswordRequired || !string.IsNullOrEmpty(_password));
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
