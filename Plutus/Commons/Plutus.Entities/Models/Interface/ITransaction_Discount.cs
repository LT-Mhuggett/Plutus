namespace Plutus.Entities.Models.Interface
{
    public interface ITransaction_Discount : IAuditable
    {
        decimal DiscountRate { get; set; }

        int TransactionId { get; set; }
        int DiscountId { get; set; }
    }
}
