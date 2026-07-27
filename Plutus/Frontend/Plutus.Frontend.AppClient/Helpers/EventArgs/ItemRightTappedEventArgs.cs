using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Helpers.EventArgs
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
