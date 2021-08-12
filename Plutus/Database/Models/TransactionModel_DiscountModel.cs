using Database.Attributes;

namespace Database.Models
{
    public class TransactionModel_DiscountModel : NotifyModelChanged
    {
        #region Fields
        decimal _discountRate;
        #region Relationships
        int _transactionId;
        int _discountId;
        #endregion
        #endregion

        #region Properties
        [Exportable]
        public decimal DiscountRate
        {
            get => _discountRate;
            set => SetProperty(ref _discountRate, value);
        }
        #region Relationships

        [Exportable]
        public int TransactionId
        {
            get => _transactionId;
            set => SetProperty(ref _transactionId, value);
        }
        public virtual TransactionModel Transaction { get; set; }

        [Exportable]
        public int DiscountId
        {
            get => _discountId;
            set => SetProperty(ref _discountId, value);
        }
        public virtual DiscountModel Discount { get; set; }
        #endregion
        #endregion
    }
}
