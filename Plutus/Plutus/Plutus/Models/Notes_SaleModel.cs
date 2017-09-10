using System;
using System.Collections.Generic;
using System.Text;

namespace Plutus.Models
{
    public class Notes_SaleModel
    {
        public string SaleId { get; set; }
        public SaleModel Sale { get; set; }

        public int NoteId { get; set; }
        public NoteModel Note { get; set; }
    }
}
