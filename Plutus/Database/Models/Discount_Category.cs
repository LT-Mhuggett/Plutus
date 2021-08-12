using Database.Attributes;
using Database.Enums;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Database.Models
{
    [Serializable]
    public class Discount_Category : BaseModel<int>, IAuditable
    {
        #region Fields
        private DateTime _startDataTime;
        private DateTime _endDataTime;
        #region Auditable
        private DateTime _created;
        private DateTime _modified;
        private string _createdBy;
        private string _modifiedBy;
        #endregion
        #region Relationships 
        private CategoryModel _cat;
        private DiscountModel _discount;
        #endregion
        #endregion

        #region Properties
        [Exportable]
        public DateTime StartDateTime
        {
            get => _startDataTime;
            set => SetProperty(ref _startDataTime, value);
        }
        [Exportable]
        public DateTime EndDateTime
        {
            get => _endDataTime;
            set => SetProperty(ref _endDataTime, value);
        }
        [NotMapped]
        public string FullDateTime => string.Format("{0} - {1}", StartDateTime, EndDateTime);

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
        public virtual CategoryModel Cat
        {
            get => _cat;
            set => SetProperty(ref _cat, value);
        }
        [DisplayFormat(ApplyFormatInEditMode = true, DataFormatString = "{0:yyyy-MM-dd}")]
        public virtual DiscountModel Discount
        {
            get => _discount;
            set => SetProperty(ref _discount, value);
        }
        #endregion
        #endregion
    }
}
