using Plutus.Entities.Models;

namespace Plutus.Repository.FormBodies
{
    public class PaymentMethodBody : FormBody<PaymentMethod>
    {
        public string Name { get; set; }
        public decimal Charge { get; set; }
        public decimal MinimumCharge { get; set; }
        public bool IsChangeable { get; set; }
        public bool IsCashBackable { get; set; }

        public override PaymentMethod GenerateEntity() => new PaymentMethod
        {
            Name = Name,
            Charge = Charge,
            MinimumCharge = MinimumCharge,
            IsChangeable = IsChangeable,
            IsCashBackable = IsCashBackable
        };
    }
}
