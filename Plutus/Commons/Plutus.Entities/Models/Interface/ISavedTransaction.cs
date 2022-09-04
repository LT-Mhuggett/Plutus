namespace Plutus.Entities.Models.Interface
{
    public interface ISavedTransaction : IBase<Guid>
    {
        string Name { get; set; }
        string Data { get; set; }
    }
}
