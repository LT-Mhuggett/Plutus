using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Stock : Auditable, IStock
    {
        #region Properties
        [Exportable]
        public int Quantity { get; set; }

        #region Relationships
        [Exportable]
        public string ItemId { get; set; }
        public virtual Item Item { get; set; }

        [Exportable]
        public string StoreId { get; set; }
        public virtual Store Store { get; set; }
        #endregion
        #endregion
    }
}
