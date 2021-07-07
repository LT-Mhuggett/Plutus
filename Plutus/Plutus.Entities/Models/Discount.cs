using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Discount : Base<int>, IDiscount
    {
        #region Properties
        [Exportable]
        [Column("Name")] 
        public string Name { get; set; }
        [Exportable]
        public bool AllApplicable { get; set; }
        [Exportable]
        public bool CanUseWithOtherDiscounts { get; set; }
        [Exportable]
        public bool AutoApply { get; set; }
        [Exportable]
        public int Type { get; set; }
        [Exportable]
        public decimal Amount { get; set; }
        [Exportable]
        public int UsesPerTransaction { get; set; }
        [Exportable]
        public int RequiredNumOfItems { get; set; }
        [NotMapped] 
        [DefaultValue(false)]
        public bool Changed { get; set; }

        #region Relationships
        [Exportable]
        public string BussinessId { get; set; }
        public virtual Bussiness Bussiness { get; set; }

        #region Collections
        public virtual ICollection<Discount_Category> DisCategoryList { get; set; }
        public virtual ICollection<Discount_Item> DisItemList { get; set; }
        public virtual ICollection<Transaction_Discount> Transaction_Discounts { get; set; }
        #endregion
        #endregion
        #endregion
    }
}
