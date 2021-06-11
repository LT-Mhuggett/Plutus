using Plutus.Entities.Attributes;
using Plutus.Entities.Enums;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// This is the store model.
    /// It is used to store all sotre details and is used to get store details form DB.
    /// This is setup to allow physical expansion with keeping one united system.
    /// </summary>
    [Serializable]
    public class Store : Address<string>, IStore
    {
        #region Properties
        [Exportable]
        public string StoreName { get; set; }
        [Exportable]
        public string StoreAbbr{get;set;}
        [Exportable]
        public string VatIN{get;set;}
        [Exportable]
        public string ContactNumber{get;set;}
        [Exportable]
        public decimal? RecMarkup{get;set;}
        [Exportable(ExportLevels.NonUserFriendly)]
        public byte[] Logo{get;set; }

        #region Relationships
        #region Collections
        public virtual ICollection<Employee> Employees { get; set; }
        public virtual ICollection<Stock> Stocks { get; set; }
        #endregion
        #endregion
        #endregion
    }

}
