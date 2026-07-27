using Database.Attributes;
using Database.Enums;
using System;
using System.Collections.Generic;

namespace Database.Models
{
    [Serializable]
    public class NoteModel : BaseModel<int>, IAuditable
    {
        #region Fields
        private string _note;
        #region Auditable
        private DateTime _created;
        private DateTime _modified;
        private string _createdBy;
        private string _modifiedBy;
        #endregion
        #endregion

        #region Properties
        [Exportable]
        public string Note
        {
            get => _note;
            set => SetProperty(ref _note, value);
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
        public virtual ICollection<Notes_SaleModel> NoteSales { get; set; }
        #endregion
        #endregion

        public NoteModel() { }

        public NoteModel(string note)
        {
            Note = note;
        }
    }
}
