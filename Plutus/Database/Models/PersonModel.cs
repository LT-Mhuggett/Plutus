using Database.Attributes;
using Database.Enums;
using System;
using System.ComponentModel.DataAnnotations;

namespace Database.Models
{
    /// <summary>
    /// This is the Default model for any person related entity.
    /// e.g. employees inherit all of persons.
    /// </summary>
    public class PersonModel : Address<string>, IAuditable
    {
        #region Fields
        private string _fName;
        private string _lName;
        private string _mobile;
        private string _eMail;
        #region Auditable
        private DateTime _created;
        private DateTime _modified;
        private string _createdBy;
        private string _modifiedBy;
        #endregion
        #endregion

        #region Properties
        [Exportable]
        public string FName
        {
            get => _fName;
            set => SetProperty(ref _fName, value);
        }
        [Exportable]
        public string LName
        {
            get => _lName;
            set => SetProperty(ref _lName, value);
        }
        [Exportable]
        public string Mobile
        {
            get => _mobile;
            set => SetProperty(ref _mobile, value);
        }
        [Exportable]
        [Required]
        public string Email
        {
            get => _eMail;
            set => SetProperty(ref _eMail, value);
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
        #endregion
    }
}
