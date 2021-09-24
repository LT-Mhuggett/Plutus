using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Notes_Sale : Auditable, INote_Sale
    {
        #region Properties
        [Exportable]
        public string SaleId { get; set; }
        public virtual Sale Sale { get; set; }
        [Exportable]
        public int NoteId { get; set; }
        public virtual Note Note { get; set; }
        #endregion
    }
}
