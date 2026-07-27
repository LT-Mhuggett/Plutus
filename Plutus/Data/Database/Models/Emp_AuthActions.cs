using Database.Attributes;
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

        #region Auditable
        private DateTime _created;
        private DateTime _modified;
        private string _createdBy;
        private string _modifiedBy;
        #endregion
        #region Relationships
        private int _authAID;
        private AuthActions _authA;
        private string _empId;
        private EmployeeModel _emp;
        #endregion
        #endregion

        #region Properties

        [Exportable(ExportLevels.NonUserFriendly)]
        [DefaultValue(Permissions.None)]
        public Permissions Permissions { get; set; }

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
        [Exportable(ExportLevels.NonUserFriendly)]
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

        [Exportable(ExportLevels.NonUserFriendly)]
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
