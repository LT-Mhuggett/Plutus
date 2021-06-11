namespace Plutus.Entities.Models.Interface
{
    public interface IAddress<T> : IBase<T>
    {
        string AdLine1 { get; set; }
        string AdLine2 { get; set; }
        string City { get; set; }
        string PostCode { get; set; }
        string Country { get; set; }
        string FullAddress { get; set; }
    }
}
