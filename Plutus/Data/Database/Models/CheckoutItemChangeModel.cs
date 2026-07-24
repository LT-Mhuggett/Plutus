using Database.Attributes;
using Database.Enums;
using System;

namespace Database.Models
{
    [Serializable]
    public class CheckoutItemChangeModel : BaseModel<int>, IAuditable
    {
        #region Fields
        private decimal _price;
        private decimal _exPrice;

        #region Auditable
        private DateTime _created;
        private DateTime _modified;
        private string _createdBy;
        private string _modifiedBy;
        #endregion
        #region Relationships
        private string _itemId;
        private ItemModel _item;
        private TransactionModel _tran;
        private RefundModel _refund;
        #endregion
        #endregion

        #region Properties
        [Exportable]
        public decimal Price
        {
            get => _price;
            set => SetProperty(ref _price, value);
        }
        [Exportable]
        public decimal ExPrice
        {
            get => _exPrice;
            set => SetProperty(ref _exPrice, value);
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
        [Exportable]
        public string ItemId
        {
            get => _itemId;
            set => SetProperty(ref _itemId, value);
        }
        public virtual ItemModel Item
        {
            get => _item;
            set => SetProperty(ref _item, value);
        }
        public virtual TransactionModel Tran
        {
            get => _tran;
            set => SetProperty(ref _tran, value);
        }
        public virtual RefundModel Refund
        {
            get => _refund;
            set => SetProperty(ref _refund, value);
        }
        #endregion
        #endregion
    }
}
