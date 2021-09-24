using NatApp.Plutus.Helpers.EventArgs;
using NatApp.Plutus.Helpers.Extensions.XAML.ListViewWithContextMenu;
using NatApp.Plutus.UWP.Implementations.UI.Controls;
using Syncfusion.ListView.XForms.UWP;
using System;
using Xamarin.Forms.Platform.UWP;

[assembly: ExportRenderer(typeof(ListViewItemExt), typeof(ListViewItemRendererRightTap))]
namespace NatApp.Plutus.UWP.Implementations.UI.Controls
{
    public class ListViewItemRendererRightTap : ListViewItemRenderer
    {
        public ListViewItemRendererRightTap() : base()
        {
            RightTapped += SfListViewItemRendererRightTap_RightTapped;
        }

        private void SfListViewItemRendererRightTap_RightTapped(object sender, Windows.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (Element is ListViewItemExt listViewItem)
            {
                var location = TransformToVisual(null).TransformPoint(e.GetPosition(this));
                var tappedEventArgs = new ItemRightTappedEventArgs(Element.BindingContext, new Xamarin.Forms.Point(location.X, location.Y));
                listViewItem.ListView.RaiseItemRightTapped(tappedEventArgs);
            }
        }
    }
}
