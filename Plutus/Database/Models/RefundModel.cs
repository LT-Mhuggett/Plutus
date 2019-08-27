using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Database.Models
{
    [Serializable]
    public class RefundModel : BaseModel<int>, IAuditable
    {
        #region Varibales
        private string _reason;
        private int _amount;

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
        public string Reason
        {
            get => _reason;
            set => SetProperty(ref _reason, value);
        }
        public int Amount
        {
            get => _amount;
            set => SetProperty(ref _amount, value);
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
