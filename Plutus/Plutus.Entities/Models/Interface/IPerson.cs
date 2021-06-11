namespace Plutus.Entities.Models.Interface
{
    public interface IPerson : IAddress<string>
    {
        string FName { get; set; }
        string LName { get; set; }
        string Mobile { get; set; }
        string Email { get; set; }
    }
}
