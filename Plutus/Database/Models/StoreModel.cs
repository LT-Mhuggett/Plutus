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
        #endregion

        #region Properties
        public string StoreName
        {
            get => _storeName;
            set => SetProperty(ref _storeName, value);
        }
        public string StoreAbbr
        {
            get => _storeAbbr;
            set => SetProperty(ref _storeAbbr, value);
        }
        public string VatIN
        {
            get => _vatIN;
            set => SetProperty(ref _vatIN, value);
        }
        public string ContactNumber
        {
            get => _contactNumber;
            set => SetProperty(ref _contactNumber, value);
        }
        public decimal? RecMarkup
        {
            get => _recMarkup;
            set => SetProperty(ref _recMarkup, value);
        }
        public byte[] Logo
        {
            get => _logo;
            set => SetProperty(ref _logo, value);
        }

        #region Relationships
        public virtual ICollection<EmployeeModel> Employees { get; set; }
        public virtual ICollection<StockModel> Stocks { get; set; }
        #endregion
        #endregion


    }

}
