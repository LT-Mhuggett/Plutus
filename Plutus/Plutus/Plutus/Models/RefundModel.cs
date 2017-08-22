using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Plutus.Models
{
    public class RefundModel
    {
        [Key]
        public int Rid { get; set; }
        public string Reason { get; set; }
        public string SaleIdReturned { get; set; }
        public string SaleId { get; set; }
        public string ItemId { get; set; }
        public int Amount { get; set; }
        public ItemModel Item { get; set; }
        public SaleModel Sale { get; set; }
        public SaleModel SaleReturned { get; set; }

    }
}
