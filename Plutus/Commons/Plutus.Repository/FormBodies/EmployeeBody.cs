using Plutus.Entities.Models;

namespace Plutus.Repository.FormBodies
{
    public class EmployeeBody : AddressBody<Employee>
    {
        public decimal Wage { get; set; }

        public int ContractedHours { get; set; }

        public string StoreId { get; set; }

        public string FName { get; set; }

        public string LName { get; set; }

        public string Mobile { get; set; }

        public string Email { get; set; }

        public string BussinessId { get; set; }

        public override Employee GenerateEntity() => new Employee
        {
            Wage = Wage,
            ContractedHours = ContractedHours,
            StoreId = StoreId,
            FName = FName,
            LName = LName,
            Mobile = Mobile,
            Email = Email,
            BussinessId = BussinessId,
            AdLine1 = AdLine1,
            AdLine2 = AdLine2,
            City = City,
            Country = Country,
            PostCode = PostCode
        };
    }
}
