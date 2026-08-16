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

        /// <summary>
        /// ⚠ REDRAW THE NOTICEBOARD WHENEVER IT CHANGES, and once on the way in — the cadence has
        /// almost certainly polled before this page was ever opened, so a banner that only reacted to
        /// future changes would stay empty until the next beat.
        /// </summary>
        protected override void OnAppearing()
        {
            base.OnAppearing();
            (BindingContext as TillViewModel)?.Notices.Redraw();
            Services.Notices.Noticeboard.Changed += OnNoticesChanged;
        }

        /// <summary>
        /// ⚠ UNSUBSCRIBE, ALWAYS. `Changed` is a STATIC event: a page that subscribes and never
        /// detaches is held alive for the life of the process, and every visit adds another handler.
        /// </summary>
        protected override void OnDisappearing()
        {
            Services.Notices.Noticeboard.Changed -= OnNoticesChanged;
            base.OnDisappearing();
        }

        /// <summary>
        /// ⚠ ARRIVES ON THE CADENCE'S BACKGROUND THREAD, so it marshals itself — `Redraw` writes an
        /// `ObservableCollection` a `CollectionView` is bound to, and doing that off the UI thread is
        /// the kind of fault that works in testing and throws on a shop floor.
        /// </summary>
        private void OnNoticesChanged() =>
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(
                () => (BindingContext as TillViewModel)?.Notices.Redraw());

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