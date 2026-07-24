using Plutus.Entities.Models;
using System;

namespace Plutus.Entities.FormBodies
{
    public class TillBody : FormBody<Till>
    {
        public Guid MachineId { get; set; }
        public int StoreId { get; set; }
        public decimal CashFloat { get; set; }
        public DateTime LastOnline { get; set; }

        public override Till GenerateEntity()
        {
            var entity = base.GenerateEntity();
            entity.StoreId = StoreId;
            entity.Id = MachineId;
            entity.CashFloat = CashFloat;
            entity.LastOnline = LastOnline;
            return entity;
        }

        public TillBody() : base()
        {

        }

        public TillBody(Till entity) : base(entity)
        {
            MachineId = entity.Id;
            StoreId = entity.StoreId;
            CashFloat = entity.CashFloat;
            LastOnline = entity.LastOnline;
        }
    }
}
