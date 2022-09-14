using Plutus.Entities.Models;

namespace Plutus.Entities.FormBodies
{
    public abstract class AddressBody<TEntity, TId> : FormBody<TEntity> where TEntity: Address<TId>, new()
    {
        public TId Id { get; set; }

        public string AdLine1 { get; set; }

        public string AdLine2 { get; set; }

        public string City { get; set; }

        public string PostCode { get; set; }

        public string Country { get; set; }

        public string? FullAddress { get; set; }

        public string ReadableAddress
        {
            get => FullAddress ?? $"{AdLine1},{(string.IsNullOrEmpty(AdLine2) ? "" : AdLine2+" ,")} {City}, {PostCode}, {Country}";
        }

        public override TEntity GenerateEntity()
        {
            var entity = base.GenerateEntity();
            entity.Id = Id;
            entity.AdLine1 = AdLine1;
            entity.AdLine2 = AdLine2;
            entity.City = City;
            entity.PostCode = PostCode;
            entity.Country = Country;
            entity.FullAddress = FullAddress;
            return entity;
        }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public AddressBody() : base()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {

        }

        public AddressBody(TEntity entity) : base(entity)
        {
            Id = entity.Id;
            AdLine1 = entity.AdLine1;
            AdLine2 = entity.AdLine2;
            City = entity.City;
            PostCode = entity.PostCode;
            Country = entity.Country;
            FullAddress = entity.FullAddress;
        }
    }
}
