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
        public string Wage { get; set; }
        public string Hours { get; set; }
        public string Role { get; set; }
        public string HashedPassword { get; set; }
        public string Salt { get; set; }

        [ForeignKey("StoreIdFK")]
        public string StoreId { get; set; }
        public StoreModel Store { get; set; }
        public SaleModel Sale { get; set; }
    }
}
