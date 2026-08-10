using Plutus.Frontend.AppClient.ViewModels.MainTill.Inventory.Items;
using System.Linq;

using Microsoft.Maui.Controls;

namespace Plutus.Frontend.AppClient.Views.MainTill.Inventory.Items
{
    public partial class ViewAllView : ContentPage
    {
        public ViewAllView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Tapping a row offers what can be done with it.
        ///
        /// ⚠ TAPPING IS A WAY IN BECAUSE THERE WAS NONE. Editing an item lived ONLY on a context
        /// menu — right-click on desktop, long-press on touch — with nothing on screen to say so.
        /// Asked what happened when he tried to edit an item, Matt's answer was "I didn't know how
        /// to open it" (2026-08-10). The visible per-row Edit button is the real answer; this is the
        /// second one, and it also carries "Add to basket".
        ///
        /// ⚠ THE SELECTION IS CLEARED IMMEDIATELY. A `CollectionView` in Single mode keeps the row
        /// highlighted, and it will NOT raise SelectionChanged for the same row twice — so without
        /// this, tapping the same item a second time does nothing at all and the operator concludes
        /// the screen has frozen. (The Syncfusion list this replaced used a tap EVENT, which had no
        /// such state.)
        /// </summary>
        private void OnRowSelected(object sender, SelectionChangedEventArgs e)
        {
            var item = e?.CurrentSelection?.FirstOrDefault() as Database.Models.ItemModel;

            if (sender is CollectionView list) list.SelectedItem = null;

            if (item is not null && BindingContext is ViewAllViewModel vm)
                vm.RowTapped(item);
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
           ((ViewAllViewModel)BindingContext).InitItems();
        }
    }
}
