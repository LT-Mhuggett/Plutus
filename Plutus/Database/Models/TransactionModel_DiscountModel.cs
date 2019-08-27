using System;
using System.Collections.Generic;
using System.Text;

namespace Database.Models
{
    public class TransactionModel_DiscountModel : NotifyModelChanged
    {
        #region Fields
        #region Relationships
        int _transactionId;
        int _discountId;
        #endregion
        #endregion

        #region Properties
        #region Relationships
        public int TransactionId
        {
            get => _transactionId;
            set => SetProperty(ref _transactionId, value);
        }
        public virtual TransactionModel Transaction { get; set; }

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
