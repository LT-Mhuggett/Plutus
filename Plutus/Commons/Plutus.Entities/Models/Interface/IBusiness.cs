namespace Plutus.Entities.Models.Interface
{
    public interface IBusiness : IAuditable
    {
        string Name { get; set; }
        string NameAbbr { get; set; }
        string VatIN { get; set; }
        decimal? RecMarkup { get; set; }
        byte[] Logo { get; set; }
    }
}
