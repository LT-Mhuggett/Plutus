using System;
using System.Collections.Generic;
using System.Text;
using Xamarin.Forms;

namespace NatApp.Plutus.Helpers.EventArgs
{
    public class ItemRightTappedEventArgs
    {
        #region Properties
        public object ItemData { get; } = null;
        public Point Position { get; }
        #endregion

        public ItemRightTappedEventArgs(object itemData, Point position)
        {
            ItemData = itemData;
            Position = position;
        }
    }

    public delegate void ItemRightTappedEventHandler(object sender, ItemRightTappedEventArgs e);
}
