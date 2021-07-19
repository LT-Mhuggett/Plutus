using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// This is the employee model to store all employee data and interact with the employee section of DB.
    /// it inherits PersonModel to improve code efficiency
    /// </summary>
    [Serializable]
    public class Employee : Person, IEmployee
    {
        #region Properties
        [Exportable]
        public decimal Wage { get; set; }
        [Exportable]
        public int ContractedHours { get; set; }
        public string HashedPassword { get; set; }
        public string Salt { get; set; }
        [Exportable]
        public string NIN { get; set; }
        [Exportable]
        public bool Active { get; set; }

        [NotMapped]
        public string FullName => string.Format("{0} {1}", LName.ToUpper(), FName);
        [NotMapped]
        public string AddressDis => string.IsNullOrEmpty(FullAddress) ? AdLine1 : FullAddress.Split(',')[0];

        #region Relationships
        [Exportable]
        [ForeignKey("StoreIdFK")]
        public string StoreId { get; set; }
        public virtual Store Store { get; set; }

        [Exportable]
        public string BussinessId { get; set; }
        public virtual Bussiness Bussiness { get; set; }

        public virtual Employee ParentUser { get; set; }

        [Exportable]
        public string ParentUserId { get; set; }

        public virtual Role Role { get; set; }

        [Exportable]
        public int RoleId { get; set; }

        #region Collections
        public virtual ICollection<Sale> Sale { get; set; }
        public virtual ICollection<Emp_AuthActions> EmpAuths { get; set; }
        public virtual ICollection<Refund> RefundsAuthorised { get; set; }
        #endregion
        #endregion
        #endregion
    }
}
