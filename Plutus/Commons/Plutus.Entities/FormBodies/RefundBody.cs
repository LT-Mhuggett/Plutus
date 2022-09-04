using Plutus.Entities.Models;
using System;

namespace Plutus.Entities.FormBodies
{
    public class RefundBody : FormBody<Refund>
    {
        public int Id { get; set; }
        public string Reason { get; set; }
        public int Amount { get; set; }
        public string ItemId { get; set; }
        public Guid BusinessId { get; set; }
        public Guid AuthoriserId { get; set; }
        public Guid SaleId { get; set; }
        public Guid SaleIdReturned { get; set; }
        public int? CheckoutItemChangeId { get; set; }

        public override Refund GenerateEntity()
        {
            var entity = base.GenerateEntity();
            entity.Id = Id;
            entity.Reason = Reason;
            entity.Amount = Amount;
            entity.ItemIdOne = ItemId;
            entity.ItemIdTwo = BusinessId;
            entity.AuthoriserId = AuthoriserId;
            entity.SaleId = SaleId;
            entity.SaleIdReturned = SaleIdReturned;
            entity.CheckoutItemChangeId = CheckoutItemChangeId;
            return entity;
        }

        public RefundBody() : base()
        {

        }

        public RefundBody(Refund entity) : base(entity)
        {
            Id = entity.Id;
            Reason = entity.Reason;
            Amount = entity.Amount;
            ItemId = entity.ItemIdOne;
            BusinessId = entity.ItemIdTwo;
            AuthoriserId = entity.AuthoriserId;
            SaleId = entity.SaleId;
            SaleIdReturned = entity.SaleIdReturned;
            CheckoutItemChangeId = entity.CheckoutItemChangeId;
        }
    }
}
