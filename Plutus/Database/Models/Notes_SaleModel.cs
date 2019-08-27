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
        #endregion

        #region Properties
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
        #endregion
    }
}
