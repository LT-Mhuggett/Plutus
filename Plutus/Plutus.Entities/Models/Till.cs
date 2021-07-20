using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Till : Base<string>, ITill
    {
        #region Properties

        [Exportable]
        public string MachineId { get; set; }

        [Exportable]
        public string StoreId { get; set; }

        public Store Store;
        [Exportable]
        public decimal CashFloat { get; set; }

        [Exportable]
        public DateTime LastOnline { get; set; }

        #region Relationships
        #region Collections
        public virtual ICollection<Transaction> Transactions { get; set; }

        #endregion
        #endregion
        #endregion
    }
}
