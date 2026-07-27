using Plutus.Frontend.AppClient.ViewModels.MainTill.Inventory.Items;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Views.MainTill.Inventory.Items
{
    public partial class AddEditView : ContentPage
    {
        private StackOrientation _orientationBase = StackOrientation.Horizontal;
        public AddEditView()
        {
            InitializeComponent();

            BindingContext = new AddEditViewModel();
        }

        public AddEditView(string itemId)
        {
            InitializeComponent();

            BindingContext = new AddEditViewModel(itemId);
        }

        private void CategoryPicker_SelectedIndexChanged(object sender, EventArgs e)
        {
            ((AddEditViewModel)BindingContext).CreateNewCategoryCommand.Execute(null);
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
}