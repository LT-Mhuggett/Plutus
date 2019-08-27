using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Database.Models
{
    [Serializable]
    public class TransactionModel : BaseModel<int>, IAuditable
    {
        #region Fields
        private int _amount;
        private decimal _itemsCostExPrice;
        private decimal _itemsCostPrice;

        #region Relationships
        private string _itemId;
        private ItemModel _item;

        private string _saleId;
        private SaleModel _sale;

        private int? _checkoutItemChangeId;
        private CheckoutItemChangeModel _checkoutItemChange;
        #endregion
        #endregion

        #region Properties
        public int Amount
        {
            get => _amount;
            set => SetProperty(ref _amount, value);
        }
        public decimal ItemCostExPrice
        {
            get => _itemsCostExPrice;
            set => SetProperty(ref _itemsCostExPrice, value);
        }
        public decimal ItemCostPrice
        {
            get => _itemsCostPrice;
            set => SetProperty(ref _itemsCostPrice, value);
        }
        [NotMapped]
        public ItemModel TempItem { get; set; }

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

        public string SaleId
        {
            get => _saleId;
            set => SetProperty(ref _saleId, value);
        }
        public virtual SaleModel Sale
        {
            get => _sale;
            set => SetProperty(ref _sale, value);
        }

        public int? CheckoutItemChangeId
        {
            get => _checkoutItemChangeId;
            set => SetProperty(ref _checkoutItemChangeId, value);
        }
        public virtual CheckoutItemChangeModel CheckoutItemChange
        {
            get => _checkoutItemChange;
            set => SetProperty(ref _checkoutItemChange, value);
        }
        public virtual ICollection<TransactionModel_DiscountModel> Transaction_Discounts { get; set; }
        #endregion
        #endregion
    }
}
