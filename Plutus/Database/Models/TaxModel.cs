using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Database.Models.Interface;

namespace Database.Models
{
    [Serializable]
    public class TaxModel : IAuditable, IBase<int>
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public double Rate { get; set; }
        
        public List<ItemModel> Items { get; set; }

        
    }
}
