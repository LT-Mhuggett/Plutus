using System;
using System.Collections.Generic;

namespace Database.Models
{
    [Serializable]
    public class CategoryModel : BaseModel<int>, IAuditable
    {
        #region Fields
        private string _name;
        private string _description;
        #endregion

        #region Properties
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }
        #region Collections
        public virtual ICollection<ItemModel> Items { get; set; }
        public virtual ICollection<Discount_Category> DisCats { get; set; }
        #endregion
        #endregion
    }
}
