using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class PaymentMethod_Sale : Auditable, IAuditable
    {
        #region Properties
        [Exportable]
        public decimal Amount { get; set; }
        [Exportable]
        public decimal Change { get; set; }

        #region Relationship
        [Exportable]
        public int PayId { get; set; }
        public virtual PaymentMethod PayMethod { get; set; }
        [Exportable]
        public string SaleId { get; set; }
        public virtual Sale Sale { get; set; }
        #endregion
        #endregion
    }
}
