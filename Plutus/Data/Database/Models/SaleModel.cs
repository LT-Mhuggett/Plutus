using Database.Attributes;
using Database.Enums;
using System;
using System.Collections.Generic;

namespace Database.Models
{
    [Serializable]
    public class SaleModel : BaseModel<string>, IAuditable
    {
        #region Fields
        private decimal _total;
        private decimal _totalExTax;
        private DateTime _dateofSale;

        #region Auditable
        private DateTime _created;
        private DateTime _modified;
        private string _createdBy;
        private string _modifiedBy;
        #endregion
        #region Relationships
        private string _employeeId;
        private EmployeeModel _employee;
        #endregion
        #endregion

        #region Properties
        [Exportable]
        public decimal Total
        {
            get => _total;
            set => SetProperty(ref _total, value);
        }
        [Exportable]
        public decimal TotalExTax
        {
            get => _totalExTax;
            set => SetProperty(ref _totalExTax, value);
        }
        [Exportable]
        public DateTime DateOfSale
        {
            get => _dateofSale;
            set => SetProperty(ref _dateofSale, value);
        }

        #region Auditable
        [Exportable]
        public DateTime Created
        {
            get => _created;
            set => SetProperty(ref _created, value);
        }
        [Exportable]
        public DateTime Modified
        {
            get => _modified;
            set => SetProperty(ref _modified, value);
        }
        [Exportable(ExportLevels.NonUserFriendly)]
        public string CreatedBy
        {
            get => _createdBy;
            set => SetProperty(ref _createdBy, value);
        }
        [Exportable(ExportLevels.NonUserFriendly)]
        public string ModifiedBy
        {
            get => _modifiedBy;
            set => SetProperty(ref _modifiedBy, value);
        }
        #endregion
        #region Relationships
        [Exportable]
        public string EmployeeId
        {
            get => _employeeId;
            set => SetProperty(ref _employeeId, value);
        }
        public virtual EmployeeModel Employee
        {
            get => _employee;
            set => SetProperty(ref _employee, value);
        }
        #region Collections
        /// <summary>
        /// Refunds performed within this sale
        /// </summary>
        public virtual ICollection<RefundModel> Refunds { get; set; }
        public virtual ICollection<TransactionModel> Transactions { get; set; }
        public virtual ICollection<PaymentMethod_SaleModel> PaySales { get; set; }
        public virtual ICollection<RefundModel> Refunded { get; set; }
        public virtual ICollection<Notes_SaleModel> Notes { get; set; }
        #endregion
        #endregion
        #endregion

        public SaleModel()
        {
            DateOfSale = DateTime.Now;
            Id = $"{DateTime.Now.Year}{DateTime.Now.Month}{DateTime.Now.Day}{DateTime.Now.Hour}{DateTime.Now.Minute}{DateTime.Now.Second}{DateTime.Now.Millisecond}";
        }
    }
}
