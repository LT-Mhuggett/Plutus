namespace Plutus.Entities.Models.Interface
{
    public interface INote_Sale : IAuditable
    {
        string SaleId { get; set; }
        int NoteId { get; set; }
    }
}
