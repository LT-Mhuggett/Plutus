using CommunityToolkit.Maui.Views;
using Plutus.Frontend.ClientUI.Core.EventArgs;
using Plutus.Frontend.ClientUI.Services.PopupSize;
using Plutus.Frontend.ClientUI.ViewModels.PopupViewModels;

namespace Plutus.Frontend.ClientUI.Pages.PopupViews;

public partial class AdjustItemPage : Popup
{
	public AdjustItemPage(IPopupSize popupSize, AdjustItemViewModel viewModel)
	{
		InitializeComponent();

        Size = new Size(400, 250);
        CanBeDismissedByTappingOutsideOfPopup = false;

		BindingContext = viewModel;

        ((AdjustItemViewModel)BindingContext).RaisePopupCloseRequest += AdjustItem_RaisePopupCloseRequest;
	}

    public void SetData(decimal price, decimal priceExTax)
    {
        ((AdjustItemViewModel)BindingContext).Price = price;
        ((AdjustItemViewModel)BindingContext).PriceExTax = priceExTax;
    }

    private void AdjustItem_RaisePopupCloseRequest(object sender, PopupCloseRequestEventArgs<Tuple<decimal, decimal>> popupCloseEventArgs)
    {
        Close(popupCloseEventArgs.PopupReturnValue);
    }
}