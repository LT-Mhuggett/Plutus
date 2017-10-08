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
        [Required]
        public string Name { get; set; }
        [Required]
        public decimal Charge { get; set; }
        [Required]
        public decimal MinimumCharge { get; set; }
        [Required]
        public bool IsChangeable { get; set; }
        [Required]
        public bool IsCashBackable { get; set; }

        public List<PaymentMethod_SaleModel> PaySales { get; set; }

        
    }
}
