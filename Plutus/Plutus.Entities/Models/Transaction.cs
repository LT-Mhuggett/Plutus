using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Transaction : Base<int>, ITransaction
    {
        #region Properties
        [Exportable]
        public int Amount { get; set; }
        [Exportable]
        public decimal ItemsCostExPrice { get; set; }
        [Exportable]
        public decimal ItemsCostPrice { get; set; }

        #region Relationships
        [Exportable]
        public string ItemId { get; set; }
        public Item Item { get; set; }

        [Exportable]
        public string SaleId { get; set; }
        public Sale Sale { get; set; }

        [Exportable]
        public int? CheckoutItemChangeId { get; set; }
        public virtual CheckoutItemChange CheckoutItemChange { get; set; }

        #region Collections
        public virtual ICollection<Transaction_Discount> Transaction_Discounts { get; set; }
        #endregion
        #endregion
        #endregion
    }
}
