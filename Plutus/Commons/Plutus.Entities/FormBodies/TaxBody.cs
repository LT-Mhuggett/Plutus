using Plutus.Entities.Models;
using System;

namespace Plutus.Entities.FormBodies
{
    public class TaxBody : FormBody<Tax>
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public double Rate { get; set; }

        public Guid BussinessId { get; set; }

        public override Tax GenerateEntity()
        {
            var entity = base.GenerateEntity();
            entity.IdOne = Id;
            entity.Name = Name;
            entity.Rate = Rate;
            entity.IdTwo = BussinessId;
            return entity;
        }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public TaxBody() : base()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {

        }

        public TaxBody(Tax entity) : base(entity)
        {
            Id = entity.IdOne;
            Name = entity.Name;
            Rate = entity.Rate;
            BussinessId = entity.IdTwo;
        }
    }
}
