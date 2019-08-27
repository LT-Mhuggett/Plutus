using NatApp.Plutus.Helpers.EventArgs;
using Syncfusion.ListView.XForms;
using System;
using System.Collections.Generic;
using System.Text;

namespace NatApp.Plutus.Helpers.Extensions.XAML.ListViewWithContextMenu
{
    public class ListViewWithContextMenu : SfListView
    {
        public event ItemRightTappedEventHandler ItemRightTapped;
        public void RaiseItemRightTapped(ItemRightTappedEventArgs e) => ItemRightTapped?.Invoke(this, e);
    }
}
