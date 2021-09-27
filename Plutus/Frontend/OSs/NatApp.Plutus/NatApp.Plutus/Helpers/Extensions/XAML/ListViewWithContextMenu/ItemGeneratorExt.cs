using Syncfusion.ListView.XForms;
using System;
using System.Collections.Generic;
using System.Text;

namespace NatApp.Plutus.Helpers.Extensions.XAML.ListViewWithContextMenu
{
    public class ItemGeneratorExt : ItemGenerator
    {
        public ListViewWithContextMenu ListView { get; set; }

        public ItemGeneratorExt(ListViewWithContextMenu listView) : base(listView)
        {
            ListView = listView;
        }

        protected override ListViewItem OnCreateListViewItem(int itemIndex, ItemType type, object data = null)
        {
            return type == ItemType.Record ? new ListViewItemExt(ListView) : base.OnCreateListViewItem(itemIndex, type, data);
        }
    }
}
