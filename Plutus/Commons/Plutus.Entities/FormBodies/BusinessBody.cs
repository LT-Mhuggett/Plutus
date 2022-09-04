using Plutus.Entities.Models;

namespace Plutus.Entities.FormBodies
{
    public class BusinessBody : FormBody<Business>
    {
        public Guid Id { get; set; }
        public string VatIN { get; set; }
        public string Name { get; set; }
        public string NameAbbr { get; set; }
        public decimal? RecMarkup { get; set; }


        public override Business GenerateEntity()
        {
            var entity = base.GenerateEntity();
            entity.Id = Id;
            entity.Name = Name;
            entity.NameAbbr = NameAbbr;
            entity.RecMarkup = RecMarkup;
            entity.VatIN = VatIN;

            return entity;
        }

        public BusinessBody() : base()
        {

        }

        public BusinessBody(Business entity) : base(entity)
        {
            Id = entity.Id;
            VatIN = entity.VatIN;
            Name = entity.Name;
            NameAbbr = entity.NameAbbr;
            RecMarkup = entity.RecMarkup;
        }
    }
}
