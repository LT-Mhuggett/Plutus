using Database.Models.Interface;
using System;
using System.Collections.Generic;
using System.Text;

namespace Database.Models
{
    public class CheckoutItemChangeModel : IAuditable, IBase<int>
    {
        public int Id { get; set; }
        public string ItemId { get; set; }
        public ItemModel Item { get; set; }
        public decimal Price { get; set; }
        public decimal ExPrice { get; set; }
        public TransactionModel Tran { get; set; }
        public RefundModel Refund { get; set; }
    }
}
