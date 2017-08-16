using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plutus.Models
{
    public class AuthActions
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; }
        public AuthLevel AuthLevel { get; set; }
        public int AuthLevelId { get; set; }
    }
}
