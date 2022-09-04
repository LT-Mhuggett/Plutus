using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;

namespace Plutus.Entities.Models
{
    public class Transaction_Discount : Auditable, ITransaction_Discount
    {
        #region Properties
        [Exportable]
        public decimal DiscountRate { get; set; }
        #region Relationships
        [Exportable]
        public int TransactionId { get; set; }
        [Exportable]
        public Guid SaleId { get; set; }
        public virtual Transaction Transaction { get; set; }

        [Exportable]
        public int DiscountId { get; set; }
        public virtual Discount Discount { get; set; }
        #endregion
        #endregion
    }
}
