using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Plutus.Models.Interface;

namespace Plutus.Models
{
    public class SaleModel : IAuditable, IBase<string>
    {
        public string Id { get; set; }

        public decimal Total { get; set; }
        public DateTime DateOfSale { get; set; }
        public string EmployeeId { get; set; }
        public EmployeeModel Employee { get; set; }
        public List<RefundModel> Refunds { get; set; }
        public List<TransactionModel> Transactions { get; set; }
        public List<PaymentMethod_SaleModel> PaySales { get; set; }
        public List<RefundModel> Refunded { get; set; }
        public List<Notes_SaleModel> Notes { get; set; }
    }
}
