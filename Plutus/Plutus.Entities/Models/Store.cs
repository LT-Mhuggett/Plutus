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
        public string ContactNumber{get;set;}

        #region Relationships

        [Exportable]
        public string BussinessId { get; set; }
        public virtual Bussiness Bussiness { get; set; }

        #region Collections
        public virtual ICollection<Sale> Sales { get; set; }
        public virtual ICollection<Employee> Employees { get; set; }
        public virtual ICollection<Stock> Stocks { get; set; }

        public virtual ICollection<Till> Tills { get; set; }

        #endregion
        #endregion
        #endregion
    }

}
