using Database.Attributes;
using Database.Enums;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Database.Models
{
    [Serializable]
    public class PaymentMethod_SaleModel : NotifyModelChanged, IAuditable
    {
        #region Fields
        private decimal _amount;
        private decimal _change;
        #region Auditable
        private DateTime _created;
        private DateTime _modified;
        private string _createdBy;
        private string _modifiedBy;
        #endregion

        #region Relationship
        private int _payId;
        private PaymentMethodModel _payMethod;
        private string _saleId;
        private SaleModel _sale;
        #endregion
        #endregion

        #region Properties
        [Exportable]
        public decimal Amount
        {
            get => _amount;
            set => SetProperty(ref _amount, value);
        }
        [Exportable]
        public decimal Change
        {
            get => _change;
            set => SetProperty(ref _change, value);
        }

        /// <summary>
        /// Required for using payment method values, when issues occur
        /// </summary>
        [NotMapped]
        public PaymentMethodModel TempPayMethod { get; set; }

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
        public int PayId
        {
            get => _payId;
            set => SetProperty(ref _payId, value);
        }
        public virtual PaymentMethodModel PayMethod
        {
            get => _payMethod;
            set => SetProperty(ref _payMethod, value);
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
        #endregion
        #endregion
    }
}
