namespace Plutus.Entities.Models.Interface
{
    public interface INote : IBase<int>
    {
        string Text { get; set; }
    }
}
