using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class CheckoutItemChange : Base<int>, ICheckoutItemChange
    {
        #region Properties
        [Exportable]
        public decimal Price { get; set; }
        [Exportable]
        public decimal ExPrice { get; set; }

        #region Relationships
        [Exportable]
        public string ItemId { get; set; }
        public virtual Item Item { get; set; }
        public virtual Transaction Transaction { get; set; }
        public virtual Refund Refund { get; set; }
        #endregion
        #endregion
    }
}
