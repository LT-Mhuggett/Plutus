using Plutus.Entities.Models;
using System;

namespace Plutus.Repository.FormBodies
{
    public class TillBody : FormBody<Till>
    {
        public string MachineId { get; set; }
        public string StoreId { get; set; }
        public decimal CashFloat { get; set; }
        public DateTime LastOnline { get; set; }

        public override Till GenerateEntity() => new Till
        {
            MachineId = MachineId,
            StoreId = StoreId,
            CashFloat = CashFloat,
            LastOnline = LastOnline
        };
    }
}
