using Plutus.Frontend.AppClient.ViewModels.MainTill.Till;
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

        /// <summary>
        /// ⚠ DIGITS ONLY, ENFORCED IN THE VIEW. This box was a Syncfusion `SfNumericEntry`, which
        /// refused non-numeric input for free; a plain `Entry` does not (2026-08-10 — Matt is not
        /// renewing the licence). Without this, typing "1o" leaves the two-way binding unable to
        /// convert, so `Quantity` silently keeps its OLD value while the box shows the new one —
        /// and the operator rings a quantity they did not choose.
        ///
        /// ⚠ Rejected characters are rolled back rather than stripped, so the caret does not jump.
        /// </summary>
        private void Quantity_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not Entry entry) return;
            if (string.IsNullOrEmpty(e.NewTextValue)) return;   // mid-edit; Unfocused restores it

            if (!e.NewTextValue.All(char.IsDigit))
                entry.Text = e.OldTextValue;
        }

        /// <summary>
        /// ⚠ AN EMPTY BOX IS NOT A QUANTITY. Clearing it to type a new number leaves "" behind, and
        /// leaving the field there would ring the next scan at whatever the binding last managed to
        /// parse. On the way out it becomes the viewmodel's value again — which the setter has
        /// already clamped to at least 1.
        /// </summary>
        private void Quantity_Unfocused(object sender, FocusEventArgs e)
        {
            if (sender is Entry entry && (BindingContext as TillViewModel) is TillViewModel vm)
                entry.Text = vm.Quantity.ToString();
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