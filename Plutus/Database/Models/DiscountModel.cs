using Database.Attributes;
using Database.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace Database.Models
{
    [Serializable]
    public class DiscountModel : BaseModel<int>, IAuditable
    {
        #region Fields
        private string _name;
        private bool _allApplicable;
        private bool _canUseWithOtherDiscounts;
        private bool _autoApply;
        private int _type;
        private decimal _amount;
        private int _usesPerTransaction;
        private int _requiredNumOfItems;
        #region Auditable
        private DateTime _created;
        private DateTime _modified;
        private string _createdBy;
        private string _modifiedBy;
        #endregion
        #endregion

        #region Properties
        [Exportable]
        [Column("Name")]
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }
        [Exportable]
        public bool AllApplicable
        {
            get => _allApplicable;
            set => SetProperty(ref _allApplicable, value);
        }
        [Exportable]
        public bool CanUseWithOtherDiscounts
        {
            get => _canUseWithOtherDiscounts;
            set => SetProperty(ref _canUseWithOtherDiscounts, value);
        }
        [Exportable]
        public bool AutoApply
        {
            get => _autoApply;
            set => SetProperty(ref _autoApply, value);
        }

        /// <summary>
        /// Type = 0 -> Fixed Cash off;
        /// Type = 1 -> Percentage off
        /// </summary> 
        [Exportable]
        public int Type
        {
            get => _type;
            set => SetProperty(ref _type, value);
        }

        [Exportable]
        public decimal Amount
        {
            get => _amount;
            set => SetProperty(ref _amount, value);
        }
        [Exportable]
        public int UsesPerTransaction
        {
            get => _usesPerTransaction;
            set => SetProperty(ref _usesPerTransaction, value);
        }
        [Exportable]
        public int RequiredNumOfItems
        {
            get => _requiredNumOfItems;
            set => SetProperty(ref _requiredNumOfItems, value);
        }
        [NotMapped]
        [DefaultValue(false)]
        public bool Changed { get; set; }

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
        public virtual ICollection<Discount_Category> DisCategoryList { get; set; }
        public virtual ICollection<Discount_Item> DisItemList { get; set; }
        public virtual ICollection<TransactionModel_DiscountModel> Transaction_Discounts { get; set; }
        #endregion
        #endregion
    }
}
