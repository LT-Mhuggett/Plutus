using System;
using System.Collections.Generic;
using System.Text;

namespace Plutus.Models
{
    public class NoteModel
    {
        public int Id { get; set; }
        public string Note { get; set; }
        
        public List<Notes_SaleModel> NoteSales { get; set; }
    }
}
