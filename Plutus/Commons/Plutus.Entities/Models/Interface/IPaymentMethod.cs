namespace Plutus.Entities.Models.Interface
{
    public interface IPaymentMethod : IBase<int>
    {
        string Name { get; set; }
        decimal Charge { get; set; }
        decimal MinimumCharge { get; set; }
        bool IsChangeable { get; set; }
        bool IsCashBackable { get; set; }
    }
}
