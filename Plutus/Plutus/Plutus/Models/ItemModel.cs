using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace Plutus.Models
{
    public class ItemModel
    {
        [Key]
        public string ItemId { get; set; }
        public string Name { get; set; }
        public string Brand { get; set; }
        public string Desc { get; set; }
        public decimal Cost { get; set; }
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public byte[] Image { get; set; }

        [ForeignKey("VatIdFK")]
        public int VatId { get; set; }
        public ItemModel Item { get; set; }

        public List<TransactionModel> Transactions { get; set; }
    }
}
