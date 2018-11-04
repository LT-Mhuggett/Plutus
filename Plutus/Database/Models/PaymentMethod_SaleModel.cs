using System;
using System.Collections.Generic;
using System.Text;
using Database.Models.Interface;

namespace Database.Models
{
    [Serializable]
    public class PaymentMethod_SaleModel : IAuditable
    {
        public int PayId { get; set; }
        public PaymentMethodModel PayMethod { get; set; }

        public string SaleId { get; set; }
        public SaleModel Sale { get; set; }
        
        public decimal Amount { get; set; }
        public decimal Change { get; set; }
    }
}
