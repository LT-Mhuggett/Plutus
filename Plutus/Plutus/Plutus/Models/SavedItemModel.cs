using System;
using System.Collections.Generic;
using System.Text;
using Plutus.Models.Interface;

namespace Plutus.Models
{
    public class SavedItemModel : IBase<int>
    {
        public int Id { get; set; }

        public int Amount { get; set; }

        public string ItemId { get; set; }
        public ItemModel Item { get; set; }

        public int SavedTransId { get; set; }

        public SavedTransactionModel SavedTrans { get; set; }
    }
}
