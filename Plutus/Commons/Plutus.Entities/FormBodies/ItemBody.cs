using Plutus.Entities.Models;
using System;

namespace Plutus.Entities.FormBodies
{
    public class ItemBody : FormBody<Item>
    {
        public string Id { get; set; }
        public string Name { get; set; }

        public string Brand { get; set; }

        public string Desc { get; set; }

        public decimal Cost { get; set; }

        public decimal ExPrice { get; set; }

        public decimal Price { get; set; }

        public byte[]? Image { get; set; }

        public int Amount { get; set; }

        public int TaxId { get; set; }

        public Guid CatId { get; set; }

        public Guid BusinessId { get; set; }

        public override Item GenerateEntity()
        {
            var entity = base.GenerateEntity();
            entity.IdOne = Id;
            entity.IdTwo = BusinessId;
            entity.Name = Name;
            entity.Brand = Brand;
            entity.Desc = Desc;
            entity.Cost = Cost;
            entity.ExPrice = ExPrice;
            entity.Price = Price;
            entity.Image = Image;
            entity.Amount = Amount;
            entity.TaxId = TaxId;
            entity.CatId = CatId;
            return entity;
        }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public ItemBody() : base()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {
            BusinessId = Guid.Empty;
        }

        public ItemBody(Item entity) : base(entity)
        {
            Id = entity.IdOne;
            Name = entity.Name;
            Brand = entity.Brand;
            Desc = entity.Desc;
            Cost = entity.Cost;
            ExPrice = entity.ExPrice;
            Price = entity.Price;
            Image = entity.Image;
            Amount = entity.Amount;
            TaxId = entity.TaxId;
            CatId = entity.CatId;
            BusinessId = entity.IdTwo;
        }
    }
}
