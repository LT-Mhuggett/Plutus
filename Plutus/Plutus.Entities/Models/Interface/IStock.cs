namespace Plutus.Entities.Models.Interface
{
    public interface IStock : ITriCompositeBase<string, string, string>
    {
        int Quantity { get; set; }
    }
}
