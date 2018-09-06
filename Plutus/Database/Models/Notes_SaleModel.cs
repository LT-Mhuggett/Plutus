using System;
using System.Collections.Generic;
using System.Text;
using Database.Models.Interface;

namespace Database.Models
{
    public class Notes_SaleModel : IAuditable
    {
        public string SaleId { get; set; }
        public SaleModel Sale { get; set; }

        public int NoteId { get; set; }
        public NoteModel Note { get; set; }
    }
}
