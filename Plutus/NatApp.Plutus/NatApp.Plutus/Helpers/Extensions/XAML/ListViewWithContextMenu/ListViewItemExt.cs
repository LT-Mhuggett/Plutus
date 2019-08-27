using Syncfusion.ListView.XForms;
using System;
using System.Collections.Generic;
using System.Text;
using Xamarin.Forms;

namespace NatApp.Plutus.Helpers.Extensions.XAML.ListViewWithContextMenu
{
    public class ListViewItemExt : ListViewItem
    {
        public ListViewWithContextMenu ListView { get; set; }
        public List<Button> Buttons { get; set; }

        public ListViewItemExt(ListViewWithContextMenu listView)
        {
            ListView = listView;
        }
    }
}
