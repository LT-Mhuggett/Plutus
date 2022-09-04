using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Category : CompositeBase<Guid, Guid>, ICategory
    {
        #region Properties
        [Exportable]
        [Required]
        public string Name { get; set; }

        [Exportable]
        [Required]
        public string Description { get; set; }

        #region Relationships
        public virtual Business Business { get; set; }
        #region Collections
        public virtual ICollection<Item> Items { get; set; }
        public virtual ICollection<Discount_Category> Discount_Categories { get; set; }
        #endregion
        #endregion
        #endregion
    }
}
