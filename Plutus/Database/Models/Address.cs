using System;
using System.Collections.Generic;
using System.Text;

namespace Database.Models
{
    public class Address
    {
        public string AdLine1 { get; set; }
        public string AdLine2 { get; set; }
        public string City { get; set; }
        public string PostCode { get; set; }
        public string Country { get; set; }
        public string FullAddress { get; set; }
    }
}
