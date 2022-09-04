namespace Plutus.Entities.Models.Interface
{
    public interface IStock : ITriCompositeBase<string, Guid, int>
    {
        int Quantity { get; set; }
    }
}
