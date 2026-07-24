using CommunityToolkit.Maui.Views;
using Plutus.Frontend.ClientUI.Services.PopupSize;
using Plutus.Frontend.ClientUI.ViewModels.PopupViewModels;

namespace Plutus.Frontend.ClientUI.Pages.PopupViews;

public partial class CategoryCreatePage : Popup
{
	public CategoryCreatePage(IPopupSize popupSize, CategoryCreateViewModel viewModel)
	{
		InitializeComponent();
		WidthRequest = 400;
		HeightRequest = 250;
		CanBeDismissedByTappingOutsideOfPopup = false;

		BindingContext = viewModel;

		(BindingContext as CategoryCreateViewModel).RaisePopupCloseRequest += CategoryCreatePage_RaisePopupCloseRequest;
	}

	public object ReturnValue { get; private set; }

	private async void CategoryCreatePage_RaisePopupCloseRequest(object sender, Core.EventArgs.PopupCloseRequestEventArgs<Entities.Models.Category> popupCloseEventArgs)
	{
		ReturnValue = popupCloseEventArgs.PopupReturnValue;
		await CloseAsync();
	}
}