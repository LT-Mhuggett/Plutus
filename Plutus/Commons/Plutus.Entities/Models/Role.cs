using Plutus.Entities.Attributes;
using Plutus.Entities.Enums;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Role : Base<int>, IRole
    {
        #region Properties
        [Exportable(ExportLevels.NonUserFriendly)]
        public string Name { get; set; }

        #region Relationships
        public virtual Business Business { get; set; }

        [Exportable]
        public Guid BusinessId { get; set; }

        [Exportable]
        public int ParentId { get; set; }
        public virtual Role ParentRole { get; set; }
        #region Collections

        public virtual ICollection<AuthActionAPIMapping> AuthActionAPIMappings { get; set; }
        public virtual ICollection<Employee> Employees { get; set; }
        #endregion
        #endregion
        #endregion
    }
}
