using System.ComponentModel.DataAnnotations.Schema;

namespace Database.Models
{
    public class Address<T> : BaseModel<T>
    {
        #region Fields
        private string _adLine1;
        private string _adLine2;
        private string _city;
        private string _postCode;
        private string _country;
        private string _fullAddress;
        #endregion

        #region Properties
        public string AdLine1
        {
            get => _adLine1;
            set => SetProperty(ref _adLine1, value);
        }
        public string AdLine2
        {
            get => _adLine2;
            set => SetProperty(ref _adLine2, value);
        }
        public string City
        {
            get => _city;
            set => SetProperty(ref _city, value);
        }
        public string PostCode
        {
            get => _postCode;
            set => SetProperty(ref _postCode, value);
        }
        public string Country
        {
            get => _country;
            set => SetProperty(ref _country, value);
        }
        public string FullAddress
        {
            get => _fullAddress;
            set => SetProperty(ref _fullAddress, value);
        }
        [NotMapped]
        public string ReadableAddress
        {
            get => _fullAddress ?? $"{AdLine1}, {AdLine2}, {City}, {PostCode}, {Country}";
        }
        #endregion
    }
}
