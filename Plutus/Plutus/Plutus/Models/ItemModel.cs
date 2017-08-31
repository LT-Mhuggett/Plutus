using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;


namespace Plutus.Models
{
    public class ItemModel
    {
        private ItemModel item;

        [Key]
        public string ItemId { get; set; }
        public string Name { get; set; }
        public string Brand { get; set; }
        public string Desc { get; set; }
        public decimal Cost { get; set; }
        public decimal Price { get; set; }
        public byte[] Image { get; set; }

        [ForeignKey("VatIdFK")]
        public int VatId { get; set; }
        [ForeignKey("CatIdFK")]
        public int CatId { get; set; }

        public List<TransactionModel> Transactions { get; set; }
        public List<RefundModel> Refunds { get; set; }
        public StockModel Stock { get; set; }
        public VatModel Vat { get; set; }
        public CategoryModel Cat { get; set; }

        public ItemModel() { }

        public ItemModel(Basket item)
        {
            ItemId = item.ItemId;
            Name = item.Name;
            Brand = item.Brand;
            Desc = item.Desc;
            Cost = item.Cost;
            Price = item.Price;
            Image = item.Image;
            VatId = item.VatId;
            CatId = item.CatId;
        }
        public ItemModel(ItemModel item)
        {
            ItemId = item.ItemId;
            Name = item.Name;
            Desc = item.Desc;
            Brand = item.Brand;
            Cost = item.Cost;
            Price = item.Price;
            Image = item.Image;
            VatId = item.VatId;
            CatId = item.CatId;
        }
    }
}
