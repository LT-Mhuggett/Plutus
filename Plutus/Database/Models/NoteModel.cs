using System;
using System.Collections.Generic;

namespace Database.Models
{
    [Serializable]
    public class NoteModel : BaseModel<int>, IAuditable
    {
        #region Fields
        private string _note;
        #endregion

        #region Properties
        public string Note
        {
            get => _note;
            set => SetProperty(ref _note, value);
        }

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
