using Database.Attributes;
using Database.Enums;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Database.Models
{
    [Serializable]
    public class RefundModel : BaseModel<int>, IAuditable
    {
        #region Fields
        private string _reason;
        private int _amount;

        #region Auditable
        private DateTime _created;
        private DateTime _modified;
        private string _createdBy;
        private string _modifiedBy;
        #endregion
        #region Relastionships
        private string _itemId;
        private ItemModel _item;

        private string _authoriserId;
        private EmployeeModel _authoriser;

        private string _saleId;
        private SaleModel _sale;

        private string _saleIdReturned;
        private SaleModel _saleReturned;

        private int? _checkoutItemChangeId;
        private CheckoutItemChangeModel _checkoutItemChange;
        #endregion
        #endregion

        #region Properties
        [Exportable]
        public string Reason
        {
            get => _reason;
            set => SetProperty(ref _reason, value);
        }
        [Exportable]
        public int Amount
        {
            get => _amount;
            set => SetProperty(ref _amount, value);
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
        public string AuthoriserId
        {
            get => _authoriserId;
            set => SetProperty(ref _authoriserId, value);
        }
        public virtual EmployeeModel Authoriser
        {
            get => _authoriser;
            set => SetProperty(ref _authoriser, value);
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
        public string SaleIdReturned
        {
            get => _saleIdReturned;
            set => SetProperty(ref _saleIdReturned, value);
        }
        public virtual SaleModel SaleReturned
        {
            get => _saleReturned;
            set => SetProperty(ref _saleReturned, value);
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
        #endregion
        #endregion
    }
}
