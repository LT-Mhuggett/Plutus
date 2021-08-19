using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Category : Base<int>, ICategory
    {
        #region Properties
        [Exportable]
        [Required]
        public string Name { get; set; }

        [Exportable]
        [Required]
        public string Description { get; set; }

        #region Relationships
        #region Collections
        public virtual ICollection<Item> Items { get; set; }
        public virtual ICollection<Discount_Category> DisCats { get; set; }
        #endregion
        #endregion
        #endregion
    }
}
