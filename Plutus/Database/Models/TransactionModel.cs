using System;
using System.Collections.Generic;
using System.Text;
using Database.Models.Interface;

namespace Database.Models
{
    public class TransactionModel : IAuditable
    {
        public string ItemId { get; set; }
        public ItemModel Item { get; set; }

        public string SaleId { get; set; }
        public SaleModel Sale { get; set; }

        public int Amount { get; set; }

        
    }
}
