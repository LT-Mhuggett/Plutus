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

        public TaxBody() : base()
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
