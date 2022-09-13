using Plutus.Frontend.ClientUI.ViewModels.MainTill.Statistics;

namespace Plutus.Frontend.ClientUI.Pages.MainTill.Statistics;

public partial class SalesReportPage : ContentPage
{
	public SalesReportPage(SalesReportViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}
}