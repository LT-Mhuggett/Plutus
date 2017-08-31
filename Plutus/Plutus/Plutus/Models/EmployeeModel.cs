using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plutus.Models
{
    /// <summary>
    /// This is the employee model to store all employee data and interact with the employee section of DB.
    /// it inherits PersonModel to improve code efficiency
    /// </summary>
    public class EmployeeModel : PersonModel
    {
        public decimal Wage { get; set; }
        public int ContractedHours { get; set; }
        public string HashedPassword { get; set; }
        public string Salt { get; set; }
        public string NIN { get; set; }
        public bool Active { get; set; }

        [ForeignKey("StoreIdFK")]
        public string StoreId { get; set; }
        public StoreModel Store { get; set; }
        public List<SaleModel> Sale { get; set; }
        public List<Emp_AuthActions> EmpAuths { get; set; }
    }
}
