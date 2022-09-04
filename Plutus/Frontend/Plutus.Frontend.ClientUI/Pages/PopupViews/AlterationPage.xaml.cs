using CommunityToolkit.Maui.Views;
using Plutus.Entities.Models;
using Plutus.Frontend.ClientUI.Domain.Models;
using Plutus.Frontend.ClientUI.Services.PopupSize;
using Plutus.Frontend.ClientUI.ViewModels.PopupViewModels;
using System.Collections.ObjectModel;

namespace Plutus.Frontend.ClientUI.Pages.PopupViews;

public partial class AlterationPage : Popup
{
	public AlterationPage(IPopupSize popupSize, AlterationViewModel viewModel, Discount discount, List<BasketItem> basketItems)
	{
		InitializeComponent();

		Size = new Size(400, 250);
		CanBeDismissedByTappingOutsideOfPopup = false;

		BindingContext = viewModel;

        (BindingContext as AlterationViewModel).RaisePopupCloseRequest += AlterationPage_RaisePopupCloseRequest;
	}

	public void SetData(Discount discount, List<BasketItem> basketItems)
	{
        (BindingContext as AlterationViewModel).Discount = discount;
        (BindingContext as AlterationViewModel).BasketItems = new ObservableCollection<BasketItem>(basketItems);
    }

    private void AlterationPage_RaisePopupCloseRequest(object sender, Core.EventArgs.PopupCloseRequestEventArgs<List<BasketAlteration>> popupCloseEventArgs)
    {
		Close(popupCloseEventArgs.PopupReturnValue);
    }
}