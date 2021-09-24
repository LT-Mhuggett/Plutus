using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Tax : CompositeBase<Guid, string>, ITax 
    {
        #region Properties
        // IdOne GUID
        [Exportable]
        [Required]
        public string Name { get; set; }

        [Exportable]
        public double Rate { get; set; }

        #region Relationships
        public virtual Bussiness Bussiness { get; set; }
        #region Collections
        public virtual ICollection<Item> Items { get; set; }
        #endregion
        #endregion
        #endregion
    }
}
