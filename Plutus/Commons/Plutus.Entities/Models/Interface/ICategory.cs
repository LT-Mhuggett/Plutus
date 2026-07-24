namespace Plutus.Entities.Models.Interface
{
    public interface ICategory : ICompositeBase<Guid, Guid>
    {
        string Name { get; set; }
        string Description { get; set; }
    }
}
