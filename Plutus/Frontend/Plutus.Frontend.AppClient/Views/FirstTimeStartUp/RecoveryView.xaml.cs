using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Views.FirstTimeStartUp
{
    public partial class RecoveryView : ContentPage
    {
        private StackOrientation _orientationBase = StackOrientation.Horizontal;
        public RecoveryView()
        {
            InitializeComponent();
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            if(width < height)
            {
                if(_orientationBase == StackOrientation.Horizontal)
                {
                    ///
                    /// Change grid layout
                    ///
                    // Move CenterColumn
                    Grid.SetRow(CenterColumn, 3);
                    Grid.SetRowSpan(CenterColumn, 1);
                    Grid.SetColumn(CenterColumn, 0);
                    Grid.SetColumnSpan(CenterColumn, 3);

                    // Move RightDataColumn
                    // ⚠ Was row 4 on a grid that defines four rows (0-3). MAUI clamps rather than
                    // throwing, so in portrait this column silently landed on top of the separator
                    // row instead of below it.
                    Grid.SetRow(RightDataColumn, 3);
                    Grid.SetRowSpan(RightDataColumn, 1);
                    Grid.SetColumn(RightDataColumn, 0);
                    Grid.SetColumnSpan(RightDataColumn, 3);

                    // Enlarge LeftDataColumn
                    Grid.SetColumnSpan(LeftDataColumn, 3);
                    Grid.SetRowSpan(LeftDataColumn, 2);

                    ///
                    /// Change Seperator
                    ///  
                    var seperator = (CenterColumn.Children.First() as BoxView);
                    seperator.HeightRequest = 1;
                    seperator.WidthRequest = 200;

                    // Set current oritentation
                    _orientationBase = StackOrientation.Vertical;
                }
            }
            else
            {
                if (_orientationBase == StackOrientation.Vertical)
                {
                    ///
                    /// Change grid layout
                    ///
                    // Decrease LeftDataColumn
                    Grid.SetColumnSpan(LeftDataColumn, 1);
                    Grid.SetRowSpan(LeftDataColumn, 3);

                    // Move CenterColumn
                    Grid.SetColumnSpan(CenterColumn, 1);
                    Grid.SetColumn(CenterColumn, 1);
                    Grid.SetRow(CenterColumn, 1);
                    Grid.SetRowSpan(CenterColumn, 3);

                    // Move RightDataColumn
                    Grid.SetColumnSpan(RightDataColumn, 1);
                    Grid.SetColumn(RightDataColumn, 2);
                    Grid.SetRow(RightDataColumn, 1);
                    Grid.SetRowSpan(RightDataColumn, 3);

                    ///
                    /// Change Seperator
                    ///
                    var seperator = (CenterColumn.Children.First() as BoxView);
                    seperator.HeightRequest = 100;
                    seperator.WidthRequest = 1;

                    // Set current orientation
                    _orientationBase = StackOrientation.Horizontal;
                }
            }
        }
    }
}