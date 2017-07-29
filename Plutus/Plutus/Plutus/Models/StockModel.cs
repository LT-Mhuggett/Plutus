using System;
using System.Collections.Generic;
using System.Text;

namespace Plutus.Models
{
    public class StockModel
    {
        public string ItemId { get; set; }
        public ItemModel Item { get; set; }

        public string StoreId { get; set; }
        public StoreModel Store { get; set; }

        public int Quantity { get; set; }
    }
}
