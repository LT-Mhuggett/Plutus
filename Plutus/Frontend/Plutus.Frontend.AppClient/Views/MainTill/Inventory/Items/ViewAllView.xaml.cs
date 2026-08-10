using Plutus.Frontend.AppClient.Helpers.Extensions.XAML.ListViewWithContextMenu;
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
    public partial class ViewAllView : ContentPage
    {
        public ViewAllView()
        {
            InitializeComponent();

            // ⚠ TAPPING A ROW IS THE WAY IN, because nothing else was.
            //
            // Editing an item lived ONLY on the context menu — right-click on desktop, long-press on
            // touch — with nothing on screen to say so. Asked what happened when he tried to edit an
            // item, Matt's answer was "I didn't know how to open it" (2026-08-10). A capability
            // reachable only by a gesture nobody advertises is a capability that does not exist.
            //
            // ⚠ The context menu is KEPT as well. It works for anyone who knows it is there, and
            // removing a working path to add a discoverable one helps nobody.
            //
            // ⚠ Subscribing alongside `SfListViewContextMenuBehavior`, which also handles
            // `ItemTapped` (to dismiss its popup). Events are multicast, so both run — and the
            // dismiss is what we want first anyway.
            ItemsListView.ItemTapped += OnRowTapped;
        }

        private void OnRowTapped(object sender, Syncfusion.Maui.ListView.ItemTappedEventArgs e)
        {
            if (BindingContext is ViewAllViewModel vm)
                vm.RowTapped(e?.DataItem as Database.Models.ItemModel);
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
           ((ViewAllViewModel)BindingContext).InitItems();
        }
    }
}