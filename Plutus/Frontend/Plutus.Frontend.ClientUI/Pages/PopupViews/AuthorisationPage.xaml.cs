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

        WidthRequest = popupSize.Medium.Width;
        HeightRequest = popupSize.Medium.Height;
        CanBeDismissedByTappingOutsideOfPopup = true;

        BindingContext = viewModel;

        ((AuthorisationViewModel)BindingContext).RaisePopupCloseRequest += AuthorisationView_RaisePopupCloseRequest;
    }

    public object ReturnValue { get; private set; }

    private async void AuthorisationView_RaisePopupCloseRequest(object sender, PopupCloseRequestEventArgs<Employee> popupCloseEventArgs)
    {
        ReturnValue = popupCloseEventArgs.PopupReturnValue;
        await CloseAsync();
    }
}