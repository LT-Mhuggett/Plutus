using System;
using System.Collections.Generic;
using System.Text;
using Plutus.Models.Interface;

namespace Plutus.Models
{
    public class NoteModel : IAuditable, IBase<int>
    {
        public int Id { get; set; }
        public string Note { get; set; }

        public List<Notes_SaleModel> NoteSales { get; set; }
    }
}
