using System;
using System.Collections.Generic;
using System.Text;

namespace Plutus.Models
{
    public class TransactionModel
    {
        public string ItemId { get; set; }
        public ItemModel Item { get; set; }

        public string SaleId { get; set; }
        public SaleModel Sale { get; set; }
    }
}
