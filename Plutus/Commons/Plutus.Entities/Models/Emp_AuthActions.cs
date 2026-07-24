using Plutus.Entities.Enums;
using Plutus.Entities.Models.Interface;
using System;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Emp_AuthActions : Auditable, IEmp_AuthActions
    {
        #region Properties
        public Permissions Permissions { get; set; }

        #region Relationships
        public int AuthAId { get; set; }
        public virtual AuthActions AuthA { get; set; }
        public Guid EmpId { get; set; }
        public virtual Employee Emp { get; set; }
        #endregion
        #endregion
    }
}
