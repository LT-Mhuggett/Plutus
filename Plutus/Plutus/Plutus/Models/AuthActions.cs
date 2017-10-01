using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Plutus.Models.Interface;

namespace Plutus.Models
{
    public class AuthActions : IAuditable, IBase<int>
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Amount { get; set; }
        public List<Emp_AuthActions> EmpAuths { get; set; }
    }
}
