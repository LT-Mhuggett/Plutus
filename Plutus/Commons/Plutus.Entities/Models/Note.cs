using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Note : Base<int>, IAuditable
    {
        #region Properties
        [Exportable]
        public string Text { get; set; }
        #region Relationships
        #region Collections
        public virtual ICollection<Notes_Sale> NoteSales { get; set; }
        #endregion
        #endregion
        #endregion

        public Note() { }

        public Note(string text)
        {
            Text = text;
        }
    }
}