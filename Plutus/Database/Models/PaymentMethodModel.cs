using Database.Attributes;
using Database.Enums;
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
        #region Auditable
        private DateTime _created;
        private DateTime _modified;
        private string _createdBy;
        private string _modifiedBy;
        #endregion
        #endregion

        #region Properties
        [Exportable]
        [Required]
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }
        [Exportable(ExportLevels.NonUserFriendly)]
        [Required]
        public decimal Charge
        {
            get => _charge;
            set => SetProperty(ref _charge, value);
        }
        [Exportable(ExportLevels.NonUserFriendly)]
        [Required]
        public decimal MinimumCharge
        {
            get => _minimumCharge;
            set => SetProperty(ref _minimumCharge, value);
        }
        [Exportable(ExportLevels.NonUserFriendly)]
        [Required]
        public bool IsChangeable
        {
            get => _isChangeable;
            set => SetProperty(ref _isChangeable, value);
        }
        [Exportable(ExportLevels.NonUserFriendly)]
        [Required]
        public bool IsCashBackable
        {
            get => _isCashBackable;
            set => SetProperty(ref _isCashBackable, value);
        }
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
        public virtual ICollection<PaymentMethod_SaleModel> PaySales { get; set; }
        #endregion
        #endregion
    }
}
