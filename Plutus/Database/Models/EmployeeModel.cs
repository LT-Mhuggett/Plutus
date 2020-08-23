using Database.Attributes;
using Database.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Database.Models
{
    /// <summary>
    /// This is the employee model to store all employee data and interact with the employee section of DB.
    /// it inherits PersonModel to improve code efficiency
    /// </summary>
    [Serializable]
    public class EmployeeModel : PersonModel
    {
        #region Fields
        private decimal _wage;
        private int _contractedHours;
        private string _hashedPassword;
        private string _salt;
        private string _nIN;
        private bool _active;

        #region Relationships
        private string _storeId;
        private StoreModel _store;
        #endregion
        #endregion

        #region Properties
        [Exportable]
        public decimal Wage
        {
            get => _wage;
            set => SetProperty(ref _wage, value);
        }
        [Exportable]
        public int ContractedHours
        {
            get => _contractedHours;
            set => SetProperty(ref _contractedHours, value);
        }
        public string HashedPassword
        {
            get => _hashedPassword;
            set => SetProperty(ref _hashedPassword, value);
        }
        public string Salt
        {
            get => _salt;
            set => SetProperty(ref _salt, value);
        }
        [Exportable]
        public string NIN
        {
            get => _nIN;
            set => SetProperty(ref _nIN, value);
        }
        public bool Active
        {
            get => _active;
            set => SetProperty(ref _active, value);
        }
        [NotMapped]
        public string FullName => string.Format("{0} {1}", LName.ToUpper(), FName);
        [NotMapped]
        public string AddressDis => string.IsNullOrEmpty(FullAddress) ? AdLine1 : FullAddress.Split(',')[0];

        #region Relationships
        [Exportable]
        [ForeignKey("StoreIdFK")]
        public string StoreId
        {
            get => _storeId;
            set => SetProperty(ref _storeId, value);
        }
        public virtual StoreModel Store
        {
            get => _store;
            set => SetProperty(ref _store, value);
        }
        public virtual ICollection<SaleModel> Sale { get; set; }
        public virtual ICollection<Emp_AuthActions> EmpAuths { get; set; }
        public virtual ICollection<RefundModel> RefundsAuthorised { get; set; }
        #endregion
        #endregion
    }
}
