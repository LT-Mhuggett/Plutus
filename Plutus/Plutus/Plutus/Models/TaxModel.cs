using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Plutus.Models.Interface;

namespace Plutus.Models
{
    public class TaxModel : IAuditable, IBase<int>
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public double Rate { get; set; }
        
        public List<ItemModel> Items { get; set; }

        
    }
}
