using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Database.Models
{
    [Serializable]
    public class Discount_Item : BaseModel<int>, IAuditable
    {
        #region Fields
        private DateTime _startDataTime;
        private DateTime _endDataTime;
        #region Relationships 
        private ItemModel _item;
        private DiscountModel _discount;
        #endregion
        #endregion

        #region Properties
        public DateTime StartDateTime
        {
            get => _startDataTime;
            set => SetProperty(ref _startDataTime, value);
        }
        public DateTime EndDateTime
        {
            get => _endDataTime;
            set => SetProperty(ref _endDataTime, value);
        }
        [NotMapped]
        public string FullDateTime => string.Format("{0} - {1}", StartDateTime, EndDateTime);

        #region Relationships
        public virtual ItemModel Item
        {
            get => _item;
            set => SetProperty(ref _item, value);
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
