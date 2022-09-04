using Plutus.Entities.Models;

namespace Plutus.Entities.FormBodies
{
    public class PaymentMethodBody : FormBody<PaymentMethod>
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Charge { get; set; }
        public decimal MinimumCharge { get; set; }
        public bool IsChangeable { get; set; }
        public bool IsCashBackable { get; set; }

        public override PaymentMethod GenerateEntity()
        {
            var entity = base.GenerateEntity();
            entity.Id = Id;
            entity.Name = Name;
            entity.Charge = Charge;
            entity.MinimumCharge = MinimumCharge;
            entity.IsChangeable = IsChangeable;
            entity.IsCashBackable = IsCashBackable;
            return entity;
        }

        public PaymentMethodBody() : base()
        {

        }

        public PaymentMethodBody(PaymentMethod entity) : base(entity)
        {
            Id = entity.Id;
            Name = entity.Name;
            Charge = entity.Charge;
            MinimumCharge = entity.MinimumCharge;
            IsChangeable = entity.IsChangeable;
            IsCashBackable = entity.IsCashBackable;
        }
    }
}
