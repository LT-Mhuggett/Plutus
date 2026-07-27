using Plutus.Frontend.AppClient.ViewModels.MainTill.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Views.MainTill.Settings
{
    public partial class SettingsView : ContentPage
    {
        private StackOrientation _orientationBase = StackOrientation.Horizontal;

        public SettingsView()
        {
            InitializeComponent();

            BindingContext = new SettingsViewModel(LeftColumn, RightColumn);
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