using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Database.Models.Interface;

namespace Database.Models
{
    [Serializable]
    public class TransactionModel : IAuditable, IBase<int>
    {
        public int Id { get; set; }

        public string ItemId { get; set; }
        public ItemModel Item { get; set; }

        public string SaleId { get; set; }
        public SaleModel Sale { get; set; }

        public int Amount { get; set; }
        public decimal ItemCostExPrice { get; set; }
        public decimal ItemCostPrice { get; set; }

        public int? CheckoutItemChangeId { get; set; }
        public CheckoutItemChangeModel CheckoutItemChange { get; set; }

        [NotMapped]
        public ItemModel TempItem { get; set; }
    }
}
