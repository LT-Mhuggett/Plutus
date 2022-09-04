using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Sale : Base<Guid>, ISale
    {
        #region Properties
        [Exportable]
        public decimal Total { get; set; }
        [Exportable]
        public decimal TotalExTax { get; set; }
        [Exportable]
        public DateTime DateOfSale { get; set; }
        #region Relationships
        [Exportable]
        public Guid EmployeeId { get; set; }
        public virtual Employee Employee { get; set; }

        [Exportable]
        public int StoreId { get; set; }
        public virtual Store Store { get; set; }

        [Exportable]
        public Guid TillId { get; set; }
        public virtual Till Till { get; set; }
        #region Collections

        /// <summary>
        /// Refunds performed within this sale
        /// </summary>
        public virtual ICollection<Refund> Refunds { get; set; }
        public virtual ICollection<Transaction> Transactions { get; set; }
        public virtual ICollection<PaymentMethod_Sale> PaySales { get; set; }
        public virtual ICollection<Refund> Refunded { get; set; }
        public virtual ICollection<Note> Notes { get; set; }
        #endregion
        #endregion
        #endregion

        public Sale()
        {
            DateOfSale = DateTime.Now;
        }
    }
}
