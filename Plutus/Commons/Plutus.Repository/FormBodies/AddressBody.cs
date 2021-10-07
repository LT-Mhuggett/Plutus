namespace Plutus.Repository.FormBodies
{
    public abstract class AddressBody<TEntity> : FormBody<TEntity>
    {

        public string AdLine1 { get; set; }

        public string AdLine2 { get; set; }

        public string City { get; set; }

        public string PostCode { get; set; }

        public string Country { get; set; }

        public string FullAddress { get; set; }

        public string ReadableAddress
        {
            get => FullAddress ?? $"{AdLine1}, {AdLine2}, {City}, {PostCode}, {Country}";
        }

    }
}
