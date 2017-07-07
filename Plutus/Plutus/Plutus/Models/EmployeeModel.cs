using System;
using System.Collections.Generic;
using System.Text;

namespace Plutus.Models
{
    /// <summary>
    /// This is the employee model to store all employee data and interact with the employee section of DB.
    /// it inherits PersonModel to improve code effiency
    /// </summary>
    class EmployeeModel : PersonModel
    {
        public string Wage { get; set; }
        public string Hours { get; set; }
        public string Role { get; set; }
        internal string Password { get; set; }
        internal string PasswordConf { get; set; }
        internal string HashedPassword { get; set; }
        internal string salt { get; set; }
    }
}
