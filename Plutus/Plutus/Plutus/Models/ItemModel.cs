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
        public byte[] Image { get; set; }
        public int VatId { get; set; }
        public int CatId { get; set; }

        public List<TransactionModel> Transactions { get; set; }
        public StockModel Stock { get; set; }
        public VatModel Vat { get; set; }
        public CategoryModel Cat { get; set; }
    }
}
