using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System.ComponentModel.DataAnnotations;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// This is the Default model for any person related entity.
    /// e.g. employees inherit all of persons.
    /// </summary>
    public class Person : Address<string>, IAuditable
    {
        #region Properties
        [Exportable]
        public string FName { get; set; }
        [Exportable]
        public string LName { get; set; }
        [Exportable]
        public string Mobile { get; set; }
        [Exportable]
        [Required]
        public string Email { get; set; }
        #endregion
    }
}
