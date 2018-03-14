using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Plutus.Models.Interface;

namespace Plutus.Models
{
    public class ItemModel : IAuditable, IBase<string>
    {
        private ItemModel item;
        
        public string Id { get; set; }
        public string Name { get; set; }
        public string Brand { get; set; }
        public string Desc { get; set; }
        [DisplayFormat(DataFormatString = "{0:#.##}", ApplyFormatInEditMode = true)]
        public decimal Cost { get; set; }
        [DisplayFormat(DataFormatString = "{0:#.##}", ApplyFormatInEditMode = true)]
        public decimal ExPrice { get; set; }
        [DisplayFormat(DataFormatString = "{0:#.##}", ApplyFormatInEditMode = true)]
        public decimal Price { get; set; }
        public byte[] Image { get; set; }

        [ForeignKey("VatIdFK")]
        public int VatId { get; set; }
        [ForeignKey("CatIdFK")]
        public int CatId { get; set; }
        
        public List<Discount_Item> DisItems { get; set; }
        public List<TransactionModel> Transactions { get; set; }
        public List<RefundModel> Refunds { get; set; }
        public StockModel Stock { get; set; }
        public TaxModel Vat { get; set; }
        public CategoryModel Cat { get; set; }

        [NotMapped]
        public char GroupKey { get; set; }
        [NotMapped]
        public int Amount { get; set; }

        

        public ItemModel() { }

        public ItemModel(Basket item)
        {
            Id = item.Id;
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
            Id = item.Id;
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
