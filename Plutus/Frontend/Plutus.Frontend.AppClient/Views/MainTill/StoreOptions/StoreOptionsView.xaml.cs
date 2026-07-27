using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Views.MainTill.StoreOptions
{
    public partial class StoreOptionsView : ContentPage
    {
        private StackOrientation _orientationBase = StackOrientation.Horizontal;
        public StoreOptionsView()
        {
            InitializeComponent();

            BindingContext = new ViewModels.MainTill.StoreOptions.StoreInformationViewModel(LeftColumn, RightColumn);
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            if (width < height)
            {
                if (_orientationBase == StackOrientation.Horizontal)
                {
                    Grid.SetRow(RightColumn, 2);
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
                    Grid.SetRow(RightColumn, 1);
                    _orientationBase = StackOrientation.Horizontal;
                }
            }
        }
    }
}