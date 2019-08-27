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

        #region Relationship
        private int _payId;
        private PaymentMethodModel _payMethod;
        private string _saleId;
        private SaleModel _sale;
        #endregion
        #endregion

        #region Properties
        public decimal Amount
        {
            get => _amount;
            set => SetProperty(ref _amount, value);
        }
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

        #region Relationships
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
