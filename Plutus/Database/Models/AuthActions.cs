using System;
using System.Collections.Generic;

namespace Database.Models
{
    [Serializable]
    public class AuthActions : BaseModel<int>, IAuditable
    {
        #region Fields
        private string _name;
        private decimal _amount;
        #endregion

        #region Properties
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }
        public decimal Amount
        {
            get => _amount;
            set => SetProperty(ref _amount, value);
        }
        #region Collections
        public virtual ICollection<Emp_AuthActions> EmpAuths { get; set; }
        #endregion
        #endregion
    }
}
