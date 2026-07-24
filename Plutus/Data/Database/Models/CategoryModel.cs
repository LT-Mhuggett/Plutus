using Database.Attributes;
using Database.Enums;
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
        #region Auditable
        private DateTime _created;
        private DateTime _modified;
        private string _createdBy;
        private string _modifiedBy;
        #endregion
        #endregion

        #region Properties
        [Exportable]
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }
        [Exportable]
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
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
        #region Collections
        public virtual ICollection<ItemModel> Items { get; set; }
        public virtual ICollection<Discount_Category> DisCats { get; set; }
        #endregion
        #endregion
    }
}
