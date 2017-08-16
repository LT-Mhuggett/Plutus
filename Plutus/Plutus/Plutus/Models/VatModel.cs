using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Plutus.Models
{
    public class VatModel 
    {
        [Key]
        public int VatId { get; set; }
        public string Name { get; set; }
        public double Rate { get; set; }
        
        public List<ItemModel> Items { get; set; }
    }
}
