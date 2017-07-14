using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Plutus.Models
{
    public class Refund_SaleModel
    {
        public string RId { get; set; }
        public RefundModel Refund { get; set; }

        public string SaleId { get; set; }
        public SaleModel Sale { get; set; }
    }
}
