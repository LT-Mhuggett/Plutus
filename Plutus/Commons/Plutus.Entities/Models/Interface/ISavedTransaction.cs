namespace Plutus.Entities.Models.Interface
{
    public interface ISavedTransaction : IBase<string>
    {
        string Name { get; set; }
        string Data { get; set; }
    }
}
