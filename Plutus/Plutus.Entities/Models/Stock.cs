using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Stock : TriCompositeBase<string, string, string>, IStock
    {
        #region Properties
        [Exportable] 
        public int Quantity { get; set; }

        #region Relationships
        public virtual Item Item { get; set; }
        public virtual Store Store { get; set; }
        #endregion
        #endregion
    }
}
