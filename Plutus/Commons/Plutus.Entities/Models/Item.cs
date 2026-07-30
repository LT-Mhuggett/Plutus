using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// Item model, where <typeparamref name="T1"></typeparamref>is EAN/UPC product code or other Priamry Identifier and <typerparamref name="T2"></typerparamref> is Business ID
    /// </summary>
    [Serializable]
    public class Item : CompositeBase<string, Guid>, IItem
    {
        [Exportable]
        [Key, MaxLength(20)]
        public override string IdOne { get; set; }

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
        public byte[]? Image { get; set; }

        /// <summary>FE5.5: don't track stock for this item — carrier bags, back-issues, anything
        /// effectively unlimited. Sales and returns are still RECORDED (so "how many sold" reporting
        /// is unaffected); only the stock ledger skips it. Existing movements are left as history.</summary>
        public bool StockUntracked { get; set; }

        /// <summary>FE5.4 the Bin: a soft delete. Non-null = binned, and the item disappears from
        /// till search, sale, the webstore feed and the default inventory views — but the row (and
        /// therefore every historic sale line that references it) stays intact. There is no hard
        /// delete anywhere; restore clears this.</summary>
        public DateTime? BinnedAtUtc { get; set; }

        [NotMapped]
        public int Amount { get; set; }

        #region Relationships

        [Exportable]
        [Required]
        public int TaxId { get; set; }
        
        public virtual Tax Tax { get; set; }

        [Exportable]
        [ForeignKey("CatIdFK")]
        [Required]
        public Guid CatId { get; set; }
        public virtual Category Cat { get; set; }
        public virtual Stock? Stock { get; set; }
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
