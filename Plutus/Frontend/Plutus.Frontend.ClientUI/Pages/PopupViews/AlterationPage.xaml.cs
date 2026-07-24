using CommunityToolkit.Maui.Views;
using Plutus.Entities.Models;
using Plutus.Frontend.ClientUI.Domain.Models;
using Plutus.Frontend.ClientUI.Services.PopupSize;
using Plutus.Frontend.ClientUI.ViewModels.PopupViewModels;
using System.Collections.ObjectModel;

namespace Plutus.Frontend.ClientUI.Pages.PopupViews;

public partial class AlterationPage : Popup
{
	public AlterationPage(IPopupSize popupSize, AlterationViewModel viewModel)
	{
		InitializeComponent();

		WidthRequest = 400;
		HeightRequest = 350;
		CanBeDismissedByTappingOutsideOfPopup = false;

		BindingContext = viewModel;

        (BindingContext as AlterationViewModel).RaisePopupCloseRequest += AlterationPage_RaisePopupCloseRequest;
	}

	public void SetData(Discount discount, List<BasketItem> basketItems)
	{
        (BindingContext as AlterationViewModel).Discount = discount;
		foreach (var basketItem in basketItems)
			(BindingContext as AlterationViewModel).BasketItems.Add(basketItem);
    }

    public object ReturnValue { get; private set; }

    private async void AlterationPage_RaisePopupCloseRequest(object sender, Core.EventArgs.PopupCloseRequestEventArgs<List<BasketAlteration>> popupCloseEventArgs)
    {
		ReturnValue = popupCloseEventArgs.PopupReturnValue;
		await CloseAsync();
    }
}