using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plutus.Models
{
    public class AuthLevel
    {
        [Key]
        public int Id { get; set; }

        public List<EmployeeModel> Emps { get; set; }
        public List<AuthActions> Actions { get; set; }
    }
}
