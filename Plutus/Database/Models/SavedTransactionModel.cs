using System;
using System.Collections.Generic;
using System.Text;
using Database.Models.Interface;

namespace Database.Models
{
    public class SavedTransactionModel : IBase<int>
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public List<SavedItemModel> SavedItems { get; set; }
    }
}
