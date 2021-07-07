using Plutus.Entities.Attributes;
using Plutus.Entities.Enums;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Bussiness : Base<string>, IBussiness
    {
        #region Properties

        [Exportable]
        public string VatIN { get; set; }

        [Exportable]
        public string Name { get; set; }

        [Exportable]
        public string NameAbbr { get; set; }

        [Exportable(ExportLevels.NonUserFriendly)]
        public byte[] Logo { get; set; }

        [Exportable]
        public decimal? RecMarkup { get; set; }

        #region Relationships
        #region Collections
        public virtual ICollection<Store> Stores { get; set; }
        public virtual ICollection<Item> Items { get; set; }
        public virtual ICollection<Tax> Taxes { get; set; }
        public virtual ICollection<Employee> Employees { get; set; }
        public virtual ICollection<Discount> Discounts { get; set; }

        #endregion 
        #endregion
        #endregion
    }
}
