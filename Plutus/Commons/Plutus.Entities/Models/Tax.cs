using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// Tax model, where <typeparamref name="T1"></typeparamref>Auto Generated int ID and <typerparamref name="T2"></typerparamref> is Business ID
    /// </summary>
    [Serializable]
    public class Tax : CompositeBase<int, Guid>, ITax 
    {
        #region Properties
        [Exportable]
        [Required]
        public string Name { get; set; }

        [Exportable]
        public double Rate { get; set; }

        #region Relationships
        public virtual Business Business { get; set; }
        #region Collections
        public virtual ICollection<Item> Items { get; set; }
        #endregion
        #endregion
        #endregion
    }
}
