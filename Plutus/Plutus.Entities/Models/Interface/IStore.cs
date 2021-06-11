namespace Plutus.Entities.Models.Interface
{
    public interface IStore : IAddress<string>
    {
        string StoreName { get; set; }
        string StoreAbbr { get; set; }
        string VatIN { get; set; }
        string ContactNumber { get; set; }
        decimal? RecMarkup { get; set; }
        byte[] Logo { get; set; }
    }
}
