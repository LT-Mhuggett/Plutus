namespace Plutus.Entities.Models.Interface
{
    public interface IStore : IAddress<string>
    {
        
        string ContactNumber { get; set; }
    }
}
