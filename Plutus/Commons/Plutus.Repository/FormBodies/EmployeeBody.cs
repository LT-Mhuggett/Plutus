using System;
using System.Collections.Generic;
using System.Text;

namespace Plutus.Repository.FormBodies
{
    public class EmployeeBody : AddressBody
    {
        public decimal Wage { get; set; }

        public int ContractedHours { get; set; }

        public string StoreId { get; set; }

        public string FName { get; set; }

        public string LName { get; set; }

        public string Mobile { get; set; }

        public string Email { get; set; }

        public string BusinessId { get; set; }
    }
}
