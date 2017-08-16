using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace Plutus.Models
{
    public class SaleModel
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public string SaleId { get; set; }

        public decimal Total { get; set; }
        public DateTime DateOfSale { get; set; }
        public string EmployeeId { get; set; }
        public EmployeeModel Employee { get; set; }
        public RefundModel Refund { get; set; }
        public List<TransactionModel> Transactions { get; set; }
        public List<PaymentMethod_SaleModel> PaySales { get; set; }
        public List<Refund_SaleModel> RefundSales { get; set; }
    }
}
