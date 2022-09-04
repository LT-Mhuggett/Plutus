using Plutus.Entities.Models;
using System;

namespace Plutus.Entities.FormBodies
{
    public class StoreBody : AddressBody<Store, int>
    {
        public int Id { get; set; }
        public string ContactNumber { get; set; }
        public Guid BusinessId { get; set; }

        public override Store GenerateEntity()
        {
            var entity = base.GenerateEntity();
            entity.Id = Id;
            entity.ContactNumber = ContactNumber;
            entity.BusinessId = BusinessId;
            return entity;
        }

        public StoreBody() : base()
        {

        }

        public StoreBody(Store entity) : base(entity)
        {
            Id = entity.Id;
            ContactNumber = entity.ContactNumber;
            BusinessId = entity.BusinessId;
        }
    }
}
