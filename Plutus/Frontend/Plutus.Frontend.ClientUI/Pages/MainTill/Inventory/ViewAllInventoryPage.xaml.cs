using Microsoft.Maui.Controls;
using Plutus.Frontend.ClientUI.Helpers;
using Plutus.Frontend.ClientUI.ViewModels.MainTill.Inventory;

namespace Plutus.Frontend.ClientUI.Pages.MainTill.Inventory;

public partial class ViewAllInventoryPage : ContentPage
{
	public ViewAllInventoryPage()
	{
		InitializeComponent();

		BindingContext = ServiceHelper.GetService<ViewAllInventoryViewModel>();
	}

    protected override void OnAppearing()
    {
        base.OnAppearing();

		((ViewAllInventoryViewModel)BindingContext).InitItems();
    }
}