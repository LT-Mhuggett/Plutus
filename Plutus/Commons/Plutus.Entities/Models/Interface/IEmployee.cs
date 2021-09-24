namespace Plutus.Entities.Models.Interface
{
    public interface IEmployee : IPerson
    {
        decimal Wage { get; set; }
        int ContractedHours { get; set; }
        string HashedPassword { get; set; }
        string Salt { get; set; }
        string NIN { get; set; }
        bool Active { get; set; }
        string StoreId { get; set; }
    }
}
