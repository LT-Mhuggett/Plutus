using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Plutus.Models
{
    public class PaymentMethodModel
    {
        [Key]
        public int PayId { get; set; }
        public string Name { get; set; }
        public double Charge { get; set; }

        public List<PaymentMethod_SaleModel> PaySales { get; set; }
    }
}
