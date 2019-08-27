using Database.Enums;
using System;
using System.ComponentModel;

namespace Database.Models
{
    [Serializable]
    public class Emp_AuthActions : NotifyModelChanged, IAuditable
    {
        #region Fields
        private Permissions _permissions;

        #region Relationships
        private int _authAID;
        private AuthActions _authA;
        private string _empId;
        private EmployeeModel _emp;
        #endregion
        #endregion

        #region Properties
        [DefaultValue(Permissions.None)]
        public Permissions Permissions { get; set; }

        #region Relationships
        public int AuthAId
        {
            get => _authAID;
            set => SetProperty(ref _authAID, value);
        }
        public virtual AuthActions Auth
        {
            get => _authA;
            set => SetProperty(ref _authA, value);
        }

        public string EmpId
        {
            get => _empId;
            set => SetProperty(ref _empId, value);
        }
        public virtual EmployeeModel Emp
        {
            get => _emp;
            set => SetProperty(ref _emp, value);
        }
        #endregion
        #endregion
    }
}
