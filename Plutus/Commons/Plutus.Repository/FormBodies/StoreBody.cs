using System;
using System.Collections.Generic;
using System.Text;

namespace Plutus.Repository.FormBodies
{
    public class StoreBody : AddressBody
    {
        public string ContactNumber { get; set; }
        public string BusinessId { get; set; }
    }
}
