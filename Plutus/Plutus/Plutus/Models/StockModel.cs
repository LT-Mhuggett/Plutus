using System;
using System.Collections.Generic;
using System.Text;
using Plutus.Models.Interface;

namespace Plutus.Models
{
    public class StockModel : IAuditable
    {
        public string ItemId { get; set; }
        public ItemModel Item { get; set; }

        public string StoreId { get; set; }
        public StoreModel Store { get; set; }

        public int Quantity { get; set; }
    }
}
