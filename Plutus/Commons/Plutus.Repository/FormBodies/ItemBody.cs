using Plutus.Entities.Models;
using System;

namespace Plutus.Repository.FormBodies
{
    public class ItemBody : FormBody<Item>
    {
        public string Name { get; set; }

        public string Brand { get; set; }

        public string Desc { get; set; }

        public decimal Cost { get; set; }

        public decimal ExPrice { get; set; }

        public decimal Price { get; set; }

        public byte[] Image { get; set; }

        public int Amount { get; set; }

        public Guid VatId { get; set; }

        public int CatId { get; set; }

        public string BussinessId { get; set; }

        public override Item GenerateEntity() => new Item
        {
            IdTwo = BussinessId,
            Name = Name,
            Brand = Brand,
            Desc = Desc,
            Cost = Cost,
            ExPrice = ExPrice,
            Price = Price,
            Image = Image,
            Amount = Amount,
            TaxId = VatId,
            CatId = CatId
        };
    }
}
