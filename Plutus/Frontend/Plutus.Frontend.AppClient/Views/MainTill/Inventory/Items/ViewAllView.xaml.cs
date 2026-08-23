using Plutus.Frontend.AppClient.ViewModels.MainTill.Inventory.Items;
using System.Linq;

using Microsoft.Maui.Controls;

namespace Plutus.Frontend.AppClient.Views.MainTill.Inventory.Items
{
    public partial class ViewAllView : ContentPage
    {
        /// <summary>
        /// ⚠ RELOADS ON APPEARING — the catalogue arrives by sync, so a list built once at sign-in
        /// would never show an item the portal added today (pitfall 17).
        ///
        /// ⚠⚠ **`onCadence: false`, and this is the case that argued for the flag.** `InitItems`
        /// replaces the whole `ObservableCollection` with up to 500 mapped rows and then fetches stock
        /// levels over the network. On a tick that would send a `CollectionView` back to the top every
        /// 60 seconds — **a worse fault than the staleness, and one we would have introduced.** The
        /// stock column is explicitly not live anyway ("∞" or "—", never a fabricated zero), so
        /// there is no silent-wrong-figure risk here of the kind the cadence exists to fix.
        /// </summary>
        private readonly Services.Sync.LiveScreen _live;

        public ViewAllView()
        {
            InitializeComponent();

            // ⚠ Resolved through `BindingContext` at refresh time, not captured now: this page's
            // context is set in XAML, and reading it in the constructor would bind to whatever
            // happened to be there — a MAUI binding failure being silent, that would show as a list
            // that simply never reloads.
            _live = new Services.Sync.LiveScreen(
                this, () => (BindingContext as ViewAllViewModel)?.InitItems(), onCadence: false);
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
            var item = e?.CurrentSelection?.FirstOrDefault() as Plutus.Frontend.AppClient.Models.InventoryRow;

            if (sender is CollectionView list) list.SelectedItem = null;

            if (item is not null && BindingContext is ViewAllViewModel vm)
                vm.RowTapped(item);
        }
    }
}
