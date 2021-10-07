using Plutus.Entities.Models;

namespace Plutus.Repository.FormBodies
{
    public class TaxBody : FormBody<Tax>
    {
        public string Name { get; set; }

        public double Rate { get; set; }

        public string BussinessId { get; set; }

        public override Tax GenerateEntity() => new Tax
        {
            Name = Name,
            Rate = Rate,
            IdTwo = BussinessId
        };
    }
}
