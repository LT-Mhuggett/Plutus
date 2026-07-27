using CommunityToolkit.Maui.Views;
using Plutus.Frontend.ClientUI.Core.EventArgs;
using Plutus.Frontend.ClientUI.Services.PopupSize;
using Plutus.Frontend.ClientUI.ViewModels.PopupViewModels;

namespace Plutus.Frontend.ClientUI.Pages.PopupViews;

public partial class ReturnItemPage : Popup
{
	public ReturnItemPage(IPopupSize popupSize, ReturnItemViewModel viewModel)
	{
		InitializeComponent();

		Size = new Size(400, 250);
		CanBeDismissedByTappingOutsideOfPopup = false;

		BindingContext = viewModel;

		((ReturnItemViewModel)BindingContext).RaisePopupCloseRequest += ReturnItem_RaisePopupCloseRequest;
	}

    private void ReturnItem_RaisePopupCloseRequest(object sender, PopupCloseRequestEventArgs<Tuple<string, string>> popupCloseEventArgs)
    {
		Close(popupCloseEventArgs.PopupReturnValue);
    }
}