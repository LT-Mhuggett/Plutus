using System;
using System.Collections.Generic;
using System.Text;

namespace Plutus.Models
{
    public class PaymentMethod_SaleModel
    {
        public int PayId { get; set; }
        public PaymentMethodModel PayMethod { get; set; }

        public string SaleId { get; set; }
        public SaleModel Sale { get; set; }
        
    }
}
