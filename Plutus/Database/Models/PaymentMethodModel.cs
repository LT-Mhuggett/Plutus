using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Database.Models
{
    [Serializable]
    public class PaymentMethodModel : BaseModel<int>, IAuditable
    {
        #region Fields
        private string _name;
        private decimal _charge;
        private decimal _minimumCharge;
        private bool _isChangeable;
        private bool _isCashBackable;
        #endregion

        #region Properties
        [Required]
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }
        [Required]
        public decimal Charge
        {
            get => _charge;
            set => SetProperty(ref _charge, value);
        }
        [Required]
        public decimal MinimumCharge
        {
            get => _minimumCharge;
            set => SetProperty(ref _minimumCharge, value);
        }
        [Required]
        public bool IsChangeable
        {
            get => _isChangeable;
            set => SetProperty(ref _isChangeable, value);
        }
        [Required]
        public bool IsCashBackable
        {
            get => _isCashBackable;
            set => SetProperty(ref _isCashBackable, value);
        }
        #region Relationships
        public virtual ICollection<PaymentMethod_SaleModel> PaySales { get; set; }
        #endregion
        #endregion
    }
}
