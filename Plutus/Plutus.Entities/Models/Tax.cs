using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Tax : Base<int>, ITax
    {
        #region Properties
        [Exportable]
        public string Name { get; set; }
        [Exportable]
        public double Rate { get; set; }

        #region Relationships
        #region Collections
        public virtual ICollection<Item> Items { get; set; }
        #endregion
        #endregion
        #endregion
    }
}
