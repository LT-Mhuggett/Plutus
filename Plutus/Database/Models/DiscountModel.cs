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
        #endregion

        #region Properties
        [Column("Name")]
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }
        public bool AllApplicable
        {
            get => _allApplicable;
            set => SetProperty(ref _allApplicable, value);
        }
        public bool CanUseWithOtherDiscounts
        {
            get => _canUseWithOtherDiscounts;
            set => SetProperty(ref _canUseWithOtherDiscounts, value);
        }
        public bool AutoApply
        {
            get => _autoApply;
            set => SetProperty(ref _autoApply, value);
        }

        /// <summary>
        /// Type = 0 -> Fixed Cash off;
        /// Type = 1 -> Percentage off
        /// </summary>
        public int Type
        {
            get => _type;
            set => SetProperty(ref _type, value);
        }

        public decimal Amount
        {
            get => _amount;
            set => SetProperty(ref _amount, value);
        }
        public int UsesPerTransaction
        {
            get => _usesPerTransaction;
            set => SetProperty(ref _usesPerTransaction, value);
        }
        public int RequiredNumOfItems
        {
            get => _requiredNumOfItems;
            set => SetProperty(ref _requiredNumOfItems, value);
        }
        [NotMapped]
        [DefaultValue(false)]
        public bool Changed { get; set; }

        #region Relationships
        public virtual ICollection<Discount_Category> DisCategoryList { get; set; }
        public virtual ICollection<Discount_Item> DisItemList { get; set; }
        public virtual ICollection<TransactionModel_DiscountModel> Transaction_Discounts { get; set; }
        #endregion
        #endregion
    }
}
