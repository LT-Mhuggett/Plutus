using Plutus.Entities.Models;
using System;

namespace Plutus.Entities.FormBodies
{
    public class EmployeeBody : AddressBody<Employee, Guid>
    {
        public Guid Id { get; set; }
        public decimal Wage { get; set; }

        public int ContractedHours { get; set; }

        public int StoreId { get; set; }

        public string FName { get; set; }

        public string LName { get; set; }

        public string Mobile { get; set; }

        public string Email { get; set; }
        public string NIN { get; set; }
        public bool active { get; set; }

        public Guid BusinessId { get; set; }

        public override Employee GenerateEntity()
        {
            var entity = base.GenerateEntity();
            entity.Id = Id;
            entity.Wage = Wage;
            entity.ContractedHours = ContractedHours;
            entity.StoreId = StoreId;
            entity.FName = FName;
            entity.LName = LName;
            entity.Mobile = Mobile;
            entity.Email = Email;
            entity.BusinessId = BusinessId;
            entity.NIN = NIN;
            entity.Active = active;
            return entity;
        }

        public EmployeeBody() : base()
        {
            active = true;
            BusinessId = Guid.Empty;
            Id = Guid.Empty;
        }

        public EmployeeBody(Employee entity) : base(entity)
        {
            Id = entity.Id;
            Wage = entity.Wage;
            ContractedHours = entity.ContractedHours;
            StoreId = entity.StoreId;
            FName = entity.FName;
            LName = entity.LName;
            Mobile = entity.Mobile;
            Email = entity.Email;
            NIN = entity.NIN;
            active = entity.Active;
            BusinessId = entity.BusinessId;
        }
    }
}
