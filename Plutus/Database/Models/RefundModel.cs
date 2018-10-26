using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Database.Models.Interface;

namespace Database.Models
{
    public class RefundModel : IAuditable, IBase<int>
    {
        public int Id { get; set; }
        public string Reason { get; set; }
        public string SaleIdReturned { get; set; }
        public string SaleId { get; set; }
        public string ItemId { get; set; }
        public int Amount { get; set; }
        public ItemModel Item { get; set; }
        public SaleModel Sale { get; set; }
        public SaleModel SaleReturned { get; set; }
        public int? CheckoutItemChangeId { get; set; }
        public CheckoutItemChangeModel CheckoutItemChange { get; set; }

        [NotMapped]
        public ItemModel TempItem { get; set; }
    }
}
