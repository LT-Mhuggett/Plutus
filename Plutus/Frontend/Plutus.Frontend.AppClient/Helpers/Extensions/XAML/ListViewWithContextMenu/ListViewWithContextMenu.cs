using Plutus.Frontend.AppClient.Helpers.EventArgs;
using Syncfusion.Maui.ListView;

namespace Plutus.Frontend.AppClient.Helpers.Extensions.XAML.ListViewWithContextMenu
{
    /// <summary>
    /// The Xamarin.Forms version customized SfListView's internal ItemGenerator/ListViewItem creation
    /// pipeline (see the now-removed ItemGeneratorExt/ListViewItemExt/ListViewItemRendererRightTap) to
    /// detect a native right-tap per row and raise ItemRightTapped. Syncfusion.Maui.ListView doesn't
    /// expose that same extensibility point, so ItemRightTapped is no longer raised by anything - only
    /// the long-press-driven ItemHolding path (still wired in SfListViewContextMenuBehavior) opens the
    /// context menu now. Kept as a no-op event so SfListViewContextMenuBehavior still compiles unchanged;
    /// needs a real replacement (e.g. a PointerGestureRecognizer on the item template) if right-click
    /// access to the context menu specifically is required going forward.
    /// </summary>
    public class ListViewWithContextMenu : SfListView
    {
        public event ItemRightTappedEventHandler ItemRightTapped;
        public void RaiseItemRightTapped(Plutus.Frontend.AppClient.Helpers.EventArgs.ItemRightTappedEventArgs e) => ItemRightTapped?.Invoke(this, e);
    }
}
