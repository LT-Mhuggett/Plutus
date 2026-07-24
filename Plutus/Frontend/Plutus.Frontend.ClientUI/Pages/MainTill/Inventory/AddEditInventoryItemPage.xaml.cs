using Plutus.Frontend.ClientUI.ViewModels.MainTill.Inventory;

namespace Plutus.Frontend.ClientUI.Pages.MainTill.Inventory;

public partial class AddEditInventoryItemPage : ContentPage
{
    private StackOrientation _orientationBase = StackOrientation.Horizontal;
	public AddEditInventoryItemPage(AddEditInventoryItemViewModel viewModel)
	{
		InitializeComponent();

        viewModel.page = this;
		BindingContext = viewModel;
	}

	public async Task ItemToEditId(string itemId)
	{
		await (BindingContext as AddEditInventoryItemViewModel).ItemToEditId(itemId);
	}
    private void CategoryPicker_SelectedIndexChanged(object sender, EventArgs e)
    {
        (BindingContext as AddEditInventoryItemViewModel).CreateCategoryCommand.Execute(null);
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
                RightColumn.Margin = new Thickness(5, 5, 5, 0);
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
                RightColumn.Margin = new Thickness(5, 0);
                _orientationBase = StackOrientation.Horizontal;
            }
        }
    }
}