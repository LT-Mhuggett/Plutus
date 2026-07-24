using Database.Attributes;
using Database.Enums;
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

        #region Auditable
        private DateTime _created;
        private DateTime _modified;
        private string _createdBy;
        private string _modifiedBy;
        #endregion
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
        [Exportable]
        public int Amount
        {
            get => _amount;
            set => SetProperty(ref _amount, value);
        }
        [Exportable]
        public decimal ItemCostExPrice
        {
            get => _itemsCostExPrice;
            set => SetProperty(ref _itemsCostExPrice, value);
        }
        [Exportable]
        public decimal ItemCostPrice
        {
            get => _itemsCostPrice;
            set => SetProperty(ref _itemsCostPrice, value);
        }
        [NotMapped]
        public ItemModel TempItem { get; set; }

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

        [Exportable]
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

        [Exportable]
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
