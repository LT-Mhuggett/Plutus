using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Database.Models.Interface;

namespace Database.Models
{
    /// <summary>
    /// This is the Default model for any person related entity.
    /// e.g. employees inherit all of persons.
    /// </summary>
    public class PersonModel : Address, IAuditable, IBase<string>
    {
        public string Id { get; set; }
        public string FName { get; set; }
        public string LName { get; set; }
        public string Mobile { get; set; }
        [Required]
        public string Email { get; set; }
    }
}
