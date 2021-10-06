using System;
using System.Collections.Generic;
using System.Text;

namespace Plutus.Repository.FormBodies
{
    public class ItemBody
    {
        public string Name { get; set; }

        public string Brand { get; set; }

        public string Desc { get; set; }
        
        public decimal Cost { get; set; }
 
        public decimal ExPrice { get; set; }
      
        public decimal Price { get; set; }
        
        public byte[] Image { get; set; }
        
        public int Amount { get; set; }

        public Guid VatId { get; set; }

        public int CatId { get; set; }

        public string BusinessId { get; set; }
    }
}
