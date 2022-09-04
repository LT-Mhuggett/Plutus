using CommunityToolkit.Maui.Views;
using Plutus.Frontend.ClientUI.Services.PopupSize;
using Plutus.Frontend.ClientUI.ViewModels.PopupViewModels;

namespace Plutus.Frontend.ClientUI.Pages.PopupViews;

public partial class CategoryCreatePage : Popup
{
	public CategoryCreatePage(IPopupSize popupSize, CategoryCreateViewModel viewModel)
	{
		InitializeComponent();
		Size = new Size(400, 250);
		CanBeDismissedByTappingOutsideOfPopup = false;

		BindingContext = viewModel;

		(BindingContext as CategoryCreateViewModel).RaisePopupCloseRequest += CategoryCreatePage_RaisePopupCloseRequest;
	}

	private void CategoryCreatePage_RaisePopupCloseRequest(object sender, Core.EventArgs.PopupCloseRequestEventArgs<Entities.Models.Category> popupCloseEventArgs)
	{
		Close(popupCloseEventArgs.PopupReturnValue);
	}
}