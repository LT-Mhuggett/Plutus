using Plutus.Entities.Models;
using System;

namespace Plutus.Entities.FormBodies
{
    public class DiscountBody : FormBody<Discount>
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool AllApplicable { get; set; }
        public bool CanUseWithOtherDiscounts { get; set; }
        public bool AutoApply { get; set; }
        public int Type { get; set; }
        public decimal Amount { get; set; }
        public int UsesPerTransaction { get; set; }
        public int RequiredNumOfItems { get; set; }
        public bool Changed { get; set; }
        public Guid BusinessId { get; set; }

        public override Discount GenerateEntity()
        {
            var entity = base.GenerateEntity();
            entity.Id = Id;
            entity.Name = Name;
            entity.AllApplicable = AllApplicable;
            entity.CanUseWithOtherDiscounts = CanUseWithOtherDiscounts;
            entity.AutoApply = AutoApply;
            entity.Type = Type;
            entity.Amount = Amount;
            entity.UsesPerTransaction = UsesPerTransaction;
            entity.RequiredNumOfItems = RequiredNumOfItems;
            entity.Changed = Changed;
            entity.BusinessId = BusinessId;
            return entity;
        }

        public DiscountBody() : base()
        {

        }

        public DiscountBody(Discount entity) : base(entity)
        {
            Id = entity.Id;
            Name = entity.Name;
            AllApplicable = entity.AllApplicable;
            CanUseWithOtherDiscounts = entity.CanUseWithOtherDiscounts;
            AutoApply = entity.AutoApply;
            Type = entity.Type;
            Amount = entity.Amount;
            UsesPerTransaction = entity.UsesPerTransaction;
            RequiredNumOfItems = entity.RequiredNumOfItems;
            Changed = entity.Changed;
            BusinessId = entity.BusinessId;
        }
    }
}
