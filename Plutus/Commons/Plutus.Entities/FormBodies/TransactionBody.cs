using Plutus.Entities.Models;
using System;
using System.Collections;

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
        public CheckoutItemChangeBody? CheckoutItemChange { get; set; }
        public ICollection<Transaction_DiscountBody>? Transaction_Discounts { get; set; }

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
            entity.CheckoutItemChange = CheckoutItemChange?.GenerateEntity();
            entity.Transaction_Discounts = Transaction_Discounts?.Select(tD => tD.GenerateEntity()).ToList();
            return entity;
        }

        public TransactionBody() : base()
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
            if (CheckoutItemChangeId != null)
                CheckoutItemChange = new CheckoutItemChangeBody(entity.CheckoutItemChange);
            Transaction_Discounts = entity.Transaction_Discounts.Select(e => new Transaction_DiscountBody(e)).ToList();
        }
    }
}
