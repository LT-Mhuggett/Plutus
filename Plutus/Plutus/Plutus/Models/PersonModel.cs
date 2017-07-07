using System;
using System.Collections.Generic;
using System.Text;

namespace Plutus.Models
{
    /// <summary>
    /// This is the Default model for any person related entity.
    /// e.g. employees inherit all of persons.
    /// </summary>
    public class PersonModel
    {
        public string FName { get; set; }
        public string LName { get; set; }
        public string Mobile { get; set; }
        public string Email { get; set; }
        public string AdLine1 { get; set; }
        public string AdLine2 { get; set; }
        public string City { get; set; }
        public string PostCode { get; set; }
        public string Country { get; set; }
    }
}
