using CommunityToolkit.Maui.Views;
using Plutus.Frontend.ClientUI.Core.EventArgs;
using Plutus.Frontend.ClientUI.Core.Models;
using Plutus.Frontend.ClientUI.Services.PopupSize;
using Plutus.Frontend.ClientUI.ViewModels.PopupViewModels;

namespace Plutus.Frontend.ClientUI.Pages.PopupViews;

public partial class AdjustItemPage : Popup
{
	// Captured from the RaisePopupCloseRequest event below (before CloseAsync fires), so callers
	// awaiting ShowPopupAsync() can read the typed result off this instance afterwards without
	// needing a generic Popup<T>/x:TypeArguments XAML change.
	public PopupReturnValue<Tuple<decimal, decimal>> Result { get; private set; }

	public AdjustItemPage(IPopupSize popupSize, AdjustItemViewModel viewModel)
	{
		InitializeComponent();

        WidthRequest = 400;
        HeightRequest = 250;
        CanBeDismissedByTappingOutsideOfPopup = false;

		BindingContext = viewModel;

        ((AdjustItemViewModel)BindingContext).RaisePopupCloseRequest += AdjustItem_RaisePopupCloseRequest;
	}

    public void SetData(decimal price, decimal priceExTax)
    {
        ((AdjustItemViewModel)BindingContext).Price = price;
        ((AdjustItemViewModel)BindingContext).PriceExTax = priceExTax;
    }

    private async void AdjustItem_RaisePopupCloseRequest(object sender, PopupCloseRequestEventArgs<Tuple<decimal, decimal>> popupCloseEventArgs)
    {
        Result = popupCloseEventArgs.PopupReturnValue;
        await CloseAsync();
    }
}