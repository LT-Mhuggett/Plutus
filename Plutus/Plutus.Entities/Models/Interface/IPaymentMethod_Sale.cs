namespace Plutus.Entities.Models.Interface
{
    public interface IPaymentMethod_Sale : IAuditable
    {
        decimal Amount { get; set; }
        decimal Change { get; set; }

        int PayId { get; set; }
        string SaleId { get; set; }
    }
}
