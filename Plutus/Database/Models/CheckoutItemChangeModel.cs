using System;

namespace Database.Models
{
    [Serializable]
    public class CheckoutItemChangeModel : BaseModel<int>, IAuditable
    {
        #region Fields
        private decimal _price;
        private decimal _exPrice;

        #region Relationships
        private string _itemId;
        private ItemModel _item;
        private TransactionModel _tran;
        private RefundModel _refund;
        #endregion
        #endregion

        #region Properties
        public decimal Price
        {
            get => _price;
            set => SetProperty(ref _price, value);
        }
        public decimal ExPrice
        {
            get => _exPrice;
            set => SetProperty(ref _exPrice, value);
        }

        #region Relationships
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
