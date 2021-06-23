using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Refund : Base<int>, IRefund
    {
        #region Properties
        [Exportable]
        public string Reason { get; set; }
        [Exportable]
        public int Amount { get; set; }

        #region Relationships
        [Exportable]
        public string ItemIdOne { get; set; }
        [Exportable]
        public string ItemIdTwo { get; set; }
        public virtual Item Item { get; set; }

        [Exportable]
        public string AuthoriserId { get; set; }
        public virtual Employee Authoriser { get; set; }

        [Exportable]
        public string SaleId { get; set; }
        public virtual Sale Sale { get; set; }

        [Exportable]
        public string SaleIdReturned { get; set; }
        public virtual Sale SaleReturned { get; set; }

        [Exportable]
        public int? CheckoutItemChangeId { get; set; }
        public virtual CheckoutItemChange CheckoutItemChange { get; set; }
        #endregion
        #endregion
    }
}
