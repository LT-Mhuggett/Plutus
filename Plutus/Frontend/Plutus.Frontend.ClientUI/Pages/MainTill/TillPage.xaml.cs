using Microsoft.Maui.Controls;
using Plutus.Frontend.ClientUI.Helpers;
using Plutus.Frontend.ClientUI.ViewModels.MainTill;

namespace Plutus.Frontend.ClientUI.Pages.MainTill;

public partial class TillPage : ContentPage
{
	public TillPage(TillViewModel viewModel)
	{
		InitializeComponent();

		BindingContext = viewModel;
	}
}