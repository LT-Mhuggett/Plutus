namespace Plutus.Entities.Models.Interface
{
    public interface ITax : IBase<int>
    {
        string Name { get; set; }
        double Rate { get; set; }
    }
}
