using System;
using System.Collections.Generic;

namespace Database.Models
{
    [Serializable]
    public class SavedTransactionModel : BaseModel<int>
    {
        #region Fields
        private string _name;
        #endregion

        #region Properties
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        #region Relationships
        public virtual ICollection<SavedItemModel> SavedItems { get; set; }
        #endregion
        #endregion
    }
}
