namespace Plutus.Entities.Models.Interface
{
    public interface IEmployee : IPerson
    {
        bool Active { get; set; }
        int ContractedHours { get; set; }
        string NIN { get; set; }
        int StoreId { get; set; }
        decimal Wage { get; set; }
    }
}
