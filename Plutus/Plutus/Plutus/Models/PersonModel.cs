using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plutus.Models
{
    /// <summary>
    /// This is the Default model for any person related entity.
    /// e.g. employees inherit all of persons.
    /// </summary>
    public class PersonModel : Address
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public string FName { get; set; }
        public string LName { get; set; }
        public string Mobile { get; set; }
        public string Email { get; set; }
    }
}
