using Plutus.Frontend.ClientUI.ViewModels.MainTill.Statistics;

namespace Plutus.Frontend.ClientUI.Pages.MainTill.Statistics;

public partial class StatisticsPage : ContentPage
{
    private StackOrientation _orientationBase = StackOrientation.Horizontal;

    public StatisticsPage(StatisticsViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
		(BindingContext as StatisticsViewModel).SetupNavigationButtons(LeftColumn, RightColumn);
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (width < height)
        {
            if (_orientationBase == StackOrientation.Horizontal)
            {
                Grid.SetRow(RightColumn, 1);
                Grid.SetColumn(RightColumn, 0);
                Grid.SetColumnSpan(RightColumn, 2);
                Grid.SetColumnSpan(LeftColumn, 2);
                _orientationBase = StackOrientation.Vertical;
            }
        }
        else
        {
            if (_orientationBase == StackOrientation.Vertical)
            {
                Grid.SetColumnSpan(LeftColumn, 1);
                Grid.SetColumnSpan(RightColumn, 1);
                Grid.SetColumn(RightColumn, 1);
                Grid.SetRow(RightColumn, 0);
                _orientationBase = StackOrientation.Horizontal;
            }
        }
    }
}