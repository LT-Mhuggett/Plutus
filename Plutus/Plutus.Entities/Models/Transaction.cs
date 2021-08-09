using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

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
        public string ItemIdOne { get; set; }
        [Exportable]
        public string ItemIdTwo { get; set; }
        public Item Item { get; set; }

        [Exportable]
        [Required]
        public string TillId { get; set; }
        public Till Till { get; set; }

        [Exportable]
        [Required]
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
