using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// This is the employee model to store all employee data and interact with the employee section of DB.
    /// it inherits PersonModel to improve code efficiency
    /// </summary>
    [Serializable]
    [Table("Employees")]
    public class Employee : Person, IEmployee
    {
        #region Properties
        [Exportable]
        public bool Active { get; set; }

        /// <summary>FE9.5: stamped whenever this user is issued a token (portal password login or
        /// till sign-in). Surfaces dormant accounts for the FE9.2 clean-up. Null = never signed in.</summary>
        public DateTime? LastLoginAtUtc { get; set; }

        [NotMapped]
        public string AddressDis => string.IsNullOrEmpty(FullAddress) ? AdLine1 : FullAddress.Split(',')[0];

        [Exportable]
        public int ContractedHours { get; set; }

        [NotMapped]
        public string FullName => $"{(LName ?? string.Empty).ToUpper()} {FName}".Trim(); // null-guarded (BugFix plan)

        [Exportable]
        public string NIN { get; set; }

        [Exportable]
        public decimal Wage { get; set; }

        #region Relationships
        public virtual Business Business { get; set; }

        [Exportable]
        [Required]
        public Guid BusinessId { get; set; }

        public virtual Employee? ManagedBy { get; set; }

        [Exportable]
        public Guid? ManagedById { get; set; }

        public virtual Role? Role { get; set; }

        [Exportable]
        public int? RoleId { get; set; }

        public virtual Store Store { get; set; }

        [Exportable]
        [ForeignKey("StoreIdFK")]
        [Required]
        public int StoreId { get; set; }
        #region Collections
        public virtual ICollection<Emp_AuthActions> EmpAuths { get; set; }
        public virtual ICollection<Refund> RefundsAuthorised { get; set; }
        public virtual ICollection<Sale> Sale { get; set; }
        #endregion
        #endregion
        #endregion
    }
}
