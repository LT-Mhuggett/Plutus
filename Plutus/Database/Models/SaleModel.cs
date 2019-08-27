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

        #region Relationships
        private string _employeeId;
        private EmployeeModel _employee;
        #endregion
        #endregion

        #region Properties
        public decimal Total
        {
            get => _total;
            set => SetProperty(ref _total, value);
        }
        public decimal TotalExTax
        {
            get => _totalExTax;
            set => SetProperty(ref _totalExTax, value);
        }
        public DateTime DateOfSale
        {
            get => _dateofSale;
            set => SetProperty(ref _dateofSale, value);
        }

        #region Relationships
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
