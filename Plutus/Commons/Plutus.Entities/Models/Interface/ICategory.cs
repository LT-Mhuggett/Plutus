namespace Plutus.Entities.Models.Interface
{
    public interface ICategory : IBase<int>
    {
        string Name { get; set; }
        string Description { get; set; }
    }
}
