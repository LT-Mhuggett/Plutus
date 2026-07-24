using Database.Attributes;
using Database.Enums;
using System;
using System.Collections.Generic;

namespace Database.Models
{
    /// <summary>
    /// This is the store model.
    /// It is used to store all sotre details and is used to get store details form DB.
    /// This is setup to allow physical expansion with keeping one united system.
    /// </summary>
    [Serializable]
    public class StoreModel : Address<string>, IAuditable
    {
        #region Fields
        private string _storeName;
        private string _storeAbbr;
        private string _vatIN;
        private string _contactNumber;
        private decimal? _recMarkup;
        private byte[] _logo;
        #region Auditable
        private DateTime _created;
        private DateTime _modified;
        private string _createdBy;
        private string _modifiedBy;
        #endregion
        #endregion

        #region Properties
        [Exportable]
        public string StoreName
        {
            get => _storeName;
            set => SetProperty(ref _storeName, value);
        }
        [Exportable]
        public string StoreAbbr
        {
            get => _storeAbbr;
            set => SetProperty(ref _storeAbbr, value);
        }
        [Exportable]
        public string VatIN
        {
            get => _vatIN;
            set => SetProperty(ref _vatIN, value);
        }
        [Exportable]
        public string ContactNumber
        {
            get => _contactNumber;
            set => SetProperty(ref _contactNumber, value);
        }
        [Exportable]
        public decimal? RecMarkup
        {
            get => _recMarkup;
            set => SetProperty(ref _recMarkup, value);
        }
        [Exportable(ExportLevels.NonUserFriendly)]
        public byte[] Logo
        {
            get => _logo;
            set => SetProperty(ref _logo, value);
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
        public virtual ICollection<EmployeeModel> Employees { get; set; }
        public virtual ICollection<StockModel> Stocks { get; set; }
        #endregion
        #endregion
    }

}
