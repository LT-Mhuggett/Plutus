using CommunityToolkit.Maui.Views;
using Plutus.Frontend.ClientUI.Core.EventArgs;
using Plutus.Frontend.ClientUI.Core.Models;
using Plutus.Frontend.ClientUI.Services.PopupSize;
using Plutus.Frontend.ClientUI.ViewModels.PopupViewModels;

namespace Plutus.Frontend.ClientUI.Pages.PopupViews;

public partial class ReturnItemPage : Popup
{
	public PopupReturnValue<Tuple<string, string>> Result { get; private set; }

	public ReturnItemPage(IPopupSize popupSize, ReturnItemViewModel viewModel)
	{
		InitializeComponent();

		WidthRequest = 400;
		HeightRequest = 250;
		CanBeDismissedByTappingOutsideOfPopup = false;

		BindingContext = viewModel;

		((ReturnItemViewModel)BindingContext).RaisePopupCloseRequest += ReturnItem_RaisePopupCloseRequest;
	}

    private async void ReturnItem_RaisePopupCloseRequest(object sender, PopupCloseRequestEventArgs<Tuple<string, string>> popupCloseEventArgs)
    {
		Result = popupCloseEventArgs.PopupReturnValue;
		await CloseAsync();
    }
}