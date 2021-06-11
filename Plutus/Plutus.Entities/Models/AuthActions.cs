using Plutus.Entities.Attributes;
using Plutus.Entities.Enums;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class AuthActions : Base<int>, IAuthActions
    {
        #region Properties
        [Exportable(ExportLevels.NonUserFriendly)]
        public string Name { get; set; }

        [Exportable(ExportLevels.NonUserFriendly)]
        public decimal Amount { get; set; }

        #region Relationships
        #region Collections
        /// <summary>
        /// Employee -> Auth Actions Relationship
        /// </summary>
        public virtual ICollection<Emp_AuthActions> EmpAuths { get; set; }
        #endregion
        #endregion
        #endregion
    }
}
