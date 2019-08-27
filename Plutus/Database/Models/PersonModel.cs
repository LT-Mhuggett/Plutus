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
        #endregion

        #region Properties
        public string FName
        {
            get => _fName;
            set => SetProperty(ref _fName, value);
        }
        public string LName
        {
            get => _lName;
            set => SetProperty(ref _lName, value);
        }
        public string Mobile
        {
            get => _mobile;
            set => SetProperty(ref _mobile, value);
        }
        [Required]
        public string Email
        {
            get => _eMail;
            set => SetProperty(ref _eMail, value);
        }
        #endregion
    }
}
