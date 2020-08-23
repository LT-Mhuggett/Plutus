using Database.Attributes;
using Database.Enums;
using System;
using System.Collections.Generic;

namespace Database.Models
{
    [Serializable]
    public class TaxModel : BaseModel<int>, IAuditable
    {
        #region Fields
        private string _name;
        private double _rate;
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
        public double Rate
        {
            get => _rate;
            set => SetProperty(ref _rate, value);
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
        public virtual ICollection<ItemModel> Items { get; set; }
        #endregion
        #endregion
    }
}
