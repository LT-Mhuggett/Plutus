using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// This is the Default model for any person related entity.
    /// e.g. employees inherit all of persons.
    /// </summary>
    [Serializable]
    [Table("People")]
    public class Person : Address<Guid>, IAuditable
    {
        // object ID => Guid
        #region Properties

        [Exportable]
        [Required]
        public string Email { get; set; }

        [Exportable]
        [Required]
        public string FName { get; set; }

        [Exportable]
        [Required]
        public string LName { get; set; }

        [Exportable]
        [Required]
        public string Mobile { get; set; }
        #endregion
    }
}
