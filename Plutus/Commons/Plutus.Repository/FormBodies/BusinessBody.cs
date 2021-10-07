using Plutus.Entities.Models;

namespace Plutus.Repository.FormBodies
{
    public class BusinessBody : FormBody<Business>
    {
        public string VatIN { get; set; }
        public string Name { get; set; }
        public string NameAbbr { get; set; }
        public byte[] Logo { get; set; }
        public decimal RecMarkup { get; set; }


        public override Business GenerateEntity() => new Business
        {
            VatIN = VatIN,
            Name = Name,
            NameAbbr = NameAbbr,
            Logo = Logo,
            RecMarkup = RecMarkup
        };
    }
}
