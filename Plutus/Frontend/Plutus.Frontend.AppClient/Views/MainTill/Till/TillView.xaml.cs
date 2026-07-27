using Plutus.Frontend.AppClient.ViewModels.MainTill.Till;
using Syncfusion.Maui.Picker;
using System;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Views.MainTill.Till
{
    public partial class TillView : ContentPage
    {
        private object _lastSelectedItem;
        public TillView()
        {
            InitializeComponent();
        }

        private void Quantity_Completed(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty((BindingContext as TillViewModel)?.ItemId))
                (BindingContext as TillViewModel)?.ManualAddCommand.Execute(null);
        }

        private void AlterationsSelected(object sender, EventArgs e)
        {
            var selectedIndex = (sender as SfPicker)?.Columns?.FirstOrDefault()?.SelectedIndex;
            if (selectedIndex != null)
                (BindingContext as TillViewModel)?.AlterTransactionCommand.Execute(selectedIndex);
        }
        private void TillList_ItemTapped(object sender, ItemTappedEventArgs e)
        {
            if (_lastSelectedItem == e.Item)
            {
                ((ListView)sender).SelectedItem = null;
                _lastSelectedItem = null;
            }
            else
            {
                _lastSelectedItem = e.Item;
            }
        }
    }
}