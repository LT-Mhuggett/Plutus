using Plutus.Entities.Models;

namespace Plutus.Repository.FormBodies
{
    public class DiscountBody : FormBody<Discount>
    {
        public string Name { get; set; }
        public bool AllApplicable { get; set; }
        public bool CanUseWithOtherDiscounts { get; set; }
        public bool AutoApply { get; set; }
        public int Type { get; set; }
        public decimal Amount { get; set; }
        public int UsesPerTransaction { get; set; }
        public int RequiredNumOfItems { get; set; }
        public bool Changed { get; set; }
        public string BusinessId { get; set; }

        public override Discount GenerateEntity() => new Discount
        {
            Name = Name,
            AllApplicable = AllApplicable,
            CanUseWithOtherDiscounts = CanUseWithOtherDiscounts,
            AutoApply = AutoApply,
            Type = Type,
            Amount = Amount,
            UsesPerTransaction = UsesPerTransaction,
            RequiredNumOfItems = RequiredNumOfItems,
            Changed = Changed,
            BussinessId = BusinessId
        };
    }
}
