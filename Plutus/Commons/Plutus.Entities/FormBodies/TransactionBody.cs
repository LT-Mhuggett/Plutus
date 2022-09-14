using Plutus.Entities.Models;
using System;

namespace Plutus.Entities.FormBodies
{
    public class TransactionBody : FormBody<Transaction>
    {
        public int Id { get; set; }
        public int Amount { get; set; }
        public decimal ItemsCostExPrice { get; set; }
        public decimal ItemsCostPrice { get; set; }
        public string ItemId { get; set; }
        public Guid BusinessId { get; set; }
        public Guid TillId { get; set; }
        public Guid SaleId { get; set; }
        public int? CheckoutItemChangeId { get; set; }

        public override Transaction GenerateEntity()
        {
            var entity = base.GenerateEntity();
            entity.IdOne = Id;
            entity.Amount = Amount;
            entity.ItemCostPrice = ItemsCostPrice;
            entity.ItemCostExPrice = ItemsCostExPrice;
            entity.ItemIdOne = ItemId;
            entity.ItemIdTwo = BusinessId;
            entity.TillId = TillId;
            entity.IdTwo = SaleId;
            entity.CheckoutItemChangeId = CheckoutItemChangeId;
            return entity;
        }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public TransactionBody() : base()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {

        }

        public TransactionBody(Transaction entity):base(entity)
        {
            Id = entity.IdOne;
            Amount = entity.Amount;
            ItemsCostExPrice = entity.ItemCostExPrice;
            ItemsCostPrice = entity.ItemCostPrice;
            ItemId = entity.ItemIdOne;
            BusinessId = entity.ItemIdTwo;
            TillId = entity.TillId;
            SaleId = entity.IdTwo;
            CheckoutItemChangeId = entity.CheckoutItemChangeId;
        }
    }
}
