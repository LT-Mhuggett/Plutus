using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Database.Models.Interface;

namespace Database.Models
{
    [Serializable]
    public class AuthActions : IAuditable, IBase<int>
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Amount { get; set; }
        public List<Emp_AuthActions> EmpAuths { get; set; }
    }
}
