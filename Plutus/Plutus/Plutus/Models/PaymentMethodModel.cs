using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Plutus.Models.Interface;

namespace Plutus.Models
{
    public class PaymentMethodModel : IAuditable,  IBase<int>
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Charge { get; set; }
        public decimal MinimumCharge { get; set; }

        public List<PaymentMethod_SaleModel> PaySales { get; set; }

        
    }
}
