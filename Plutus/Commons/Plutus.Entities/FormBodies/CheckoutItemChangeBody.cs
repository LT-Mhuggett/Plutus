using Plutus.Entities.Models;

namespace Plutus.Entities.FormBodies
{
    public class CheckoutItemChangeBody : FormBody<CheckoutItemChange>
    {
        public int Id { get; set; }
        public decimal Price { get; set; }
        public decimal ExPrice { get; set; }
        public string ItemIdOne { get; set; }
        public Guid ItemIdTwo { get; set; }

        public override CheckoutItemChange GenerateEntity()
        {
            var entity = base.GenerateEntity();
            entity.Id = Id;
            entity.Price = Price;
            entity.ExPrice = ExPrice;
            entity.ItemIdOne = ItemIdOne;
            entity.ItemIdTwo = ItemIdTwo;
            return entity;
        }

        public CheckoutItemChangeBody() : base()
        {

        }

        public CheckoutItemChangeBody(CheckoutItemChange entity) : base(entity)
        {
            Id = entity.Id;
            Price = entity.Price;
            ExPrice = entity.ExPrice;
            ItemIdOne = entity.ItemIdOne;
            ItemIdTwo = entity.ItemIdTwo;
        }
    }
}
