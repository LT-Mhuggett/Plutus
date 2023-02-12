using Plutus.Entities.Models;
using System;

namespace Plutus.Entities.FormBodies
{
    public class StockBody : FormBody<Stock>
    {
        public string Id { get; set; }
        public int Quantity { get; set; }
        public string ItemIdOne { get; set; }
        public Guid BussinessId { get; set; }
        public int StoreId { get; set; }

        public override Stock GenerateEntity()
        {
            var entity = base.GenerateEntity();
            entity.IdOne = ItemIdOne;
            entity.IdTwo = BussinessId;
            entity.IdThree = StoreId;
            entity.Quantity = Quantity;
            return entity;
        }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public StockBody() : base()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {

        }

        public StockBody(Stock entity) : base(entity)
        {
            Id = entity.IdOne;
            Quantity = entity.Quantity;
            ItemIdOne = entity.IdOne;
            BussinessId = entity.IdTwo;
            StoreId = entity.IdThree;
        }
    }
}
