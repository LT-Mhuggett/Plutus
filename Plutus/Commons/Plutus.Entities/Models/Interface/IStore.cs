namespace Plutus.Entities.Models.Interface
{
    public interface IStore : IAddress<int>
    {
        
        string ContactNumber { get; set; }
    }
}
