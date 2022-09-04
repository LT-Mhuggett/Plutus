using CommunityToolkit.Maui.Views;
using Plutus.Entities.Models;
using Plutus.Frontend.ClientUI.ViewModels.PopupViewModels;

namespace Plutus.Frontend.ClientUI.Pages.PopupViews;

public partial class MoniesInputPage : Popup
{
	public MoniesInputPage(MoniesInputViewModel viewModel)
	{
		InitializeComponent();
		Size = new Size(400, 350);
		CanBeDismissedByTappingOutsideOfPopup = false;
		BindingContext = viewModel;
		(BindingContext as MoniesInputViewModel).RaisePopupCloseRequest += MoniesInputPage_RaisePopupCloseRequest;
	}

	public void SetData(PaymentMethod paymentMethod, decimal amountRemaining)
	{
		(BindingContext as MoniesInputViewModel).PaymentMethod = paymentMethod;
        (BindingContext as MoniesInputViewModel).AmountRemaining = amountRemaining;
    }

    private void MoniesInputPage_RaisePopupCloseRequest(object sender, Core.EventArgs.PopupCloseRequestEventArgs<decimal> popupCloseEventArgs)
	{
		Close(popupCloseEventArgs.PopupReturnValue);
	}
}