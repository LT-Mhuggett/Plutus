using CommunityToolkit.Maui.Views;
using Plutus.Entities.Models;
using Plutus.Frontend.ClientUI.ViewModels.PopupViewModels;

namespace Plutus.Frontend.ClientUI.Pages.PopupViews;

public partial class MoniesInputPage : Popup
{
	public MoniesInputPage(MoniesInputViewModel viewModel)
	{
		InitializeComponent();
		WidthRequest = 400;
		HeightRequest = 350;
		CanBeDismissedByTappingOutsideOfPopup = false;
		BindingContext = viewModel;
		(BindingContext as MoniesInputViewModel).RaisePopupCloseRequest += MoniesInputPage_RaisePopupCloseRequest;
	}

	public void SetData(PaymentMethod paymentMethod, decimal amountRemaining)
	{
		(BindingContext as MoniesInputViewModel).PaymentMethod = paymentMethod;
        (BindingContext as MoniesInputViewModel).AmountRemaining = amountRemaining;
    }

    public object ReturnValue { get; private set; }

    private async void MoniesInputPage_RaisePopupCloseRequest(object sender, Core.EventArgs.PopupCloseRequestEventArgs<decimal> popupCloseEventArgs)
	{
		ReturnValue = popupCloseEventArgs.PopupReturnValue;
		await CloseAsync();
	}
}