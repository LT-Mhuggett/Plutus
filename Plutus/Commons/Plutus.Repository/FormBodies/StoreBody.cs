using Plutus.Entities.Models;

namespace Plutus.Repository.FormBodies
{
    public class StoreBody : AddressBody<Store>
    {
        public string ContactNumber { get; set; }
        public string BussinessId { get; set; }

        public override Store GenerateEntity() => new Store
        {
            ContactNumber = ContactNumber,
            BussinessId = BussinessId
        };
    }
}
