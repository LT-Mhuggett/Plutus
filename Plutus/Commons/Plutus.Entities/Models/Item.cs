using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Item : CompositeBase<string, string>, IItem
    {
        // Generic Base with 2 Generic Parameters
        #region Properties
        [Exportable]
        [Required]
        public string Name { get; set; }

        [Exportable]
        [Required]
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
        [Required]
        public Guid TaxId { get; set; }
        
        public virtual Tax Tax { get; set; }

        [Exportable]
        [ForeignKey("CatIdFK")]
        [Required]
        public int CatId { get; set; }
        public virtual Category Cat { get; set; }
        public virtual Stock Stock { get; set; }
        public virtual Business Business { get; set; }

        //BusinessId, Id => composite key
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
