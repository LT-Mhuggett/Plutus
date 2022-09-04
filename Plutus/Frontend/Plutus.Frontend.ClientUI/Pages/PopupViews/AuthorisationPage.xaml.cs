using CommunityToolkit.Maui.Views;
using Plutus.Entities.Models;
using Plutus.Frontend.ClientUI.Core.EventArgs;
using Plutus.Frontend.ClientUI.Services.PopupSize;
using Plutus.Frontend.ClientUI.ViewModels.PopupViewModels;

namespace Plutus.Frontend.ClientUI.Pages.PopupViews;

public partial class AuthorisationPage : Popup
{
    public AuthorisationPage(IPopupSize popupSize, AuthorisationViewModel viewModel)
    {
        InitializeComponent();

        Size = popupSize.Medium;
        CanBeDismissedByTappingOutsideOfPopup = true;

        BindingContext = viewModel;

        ((AuthorisationViewModel)BindingContext).RaisePopupCloseRequest += AuthorisationView_RaisePopupCloseRequest;
    }

    private void AuthorisationView_RaisePopupCloseRequest(object sender, PopupCloseRequestEventArgs<Employee> popupCloseEventArgs)
    {
        Close(popupCloseEventArgs.PopupReturnValue);
    }
}