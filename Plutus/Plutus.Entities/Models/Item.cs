using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Item : CompositeBase<string, string>, IItem
    {
        // Generic Base with 2 Generic Parameters
        #region Properties
        [Exportable]
        public string Name { get; set; }
        [Exportable]
        public string Brand { get; set; }
        [Exportable]
        public string Desc { get; set; }
        [Exportable]
        public decimal Cost { get; set; }
        [Exportable]
        public decimal ExPrice { get; set; }
        [Exportable]
        public decimal Price { get; set; }
        [Exportable]
        public byte[] Image { get; set; }
        [NotMapped]
        public int Amount { get; set; }
        #region Relationships
        [Exportable]
        [ForeignKey("VatIdFK")]
        public int VatId { get; set; }
        public virtual Tax Vat { get; set; }
        [Exportable]
        [ForeignKey("CatIdFK")]
        public int CatId { get; set; }
        public virtual Category Cat { get; set; }
        public virtual Stock Stock { get; set; }
        public virtual Bussiness Bussiness { get; set; }

        //BussinessId, Id => composite key
        #region Collections
        public virtual ICollection<Discount_Item> DisItems { get; set; }
        public virtual ICollection<Transaction> Transactions { get; set; }
        public virtual ICollection<Refund> Refunds { get; set; }
        public virtual ICollection<CheckoutItemChange> CheckoutItemChanges { get; set; }
        #endregion
        #endregion
        #endregion
    }
}
