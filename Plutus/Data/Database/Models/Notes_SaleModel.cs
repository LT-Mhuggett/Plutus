using Database.Attributes;
using Database.Enums;
using System;

namespace Database.Models
{
    [Serializable]
    public class Notes_SaleModel : NotifyModelChanged, IAuditable
    {
        #region Fields
        private string _saleId;
        private SaleModel _sale;
        private int _noteId;
        private NoteModel _note;
        #region Auditable
        private DateTime _created;
        private DateTime _modified;
        private string _createdBy;
        private string _modifiedBy;
        #endregion
        #endregion

        #region Properties
        [Exportable]
        public string SaleId
        {
            get => _saleId;
            set => SetProperty(ref _saleId, value);
        }
        public virtual SaleModel Sale
        {
            get => _sale;
            set => SetProperty(ref _sale, value);
        }

        [Exportable]
        public int NoteId
        {
            get => _noteId;
            set => SetProperty(ref _noteId, value);
        }
        public virtual NoteModel Note
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
        #endregion
    }
}
