using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Database.Models.Interface;

namespace Database.Models
{
    [Serializable]
    public class Emp_AuthActions : IAuditable
    {
        public int AuthAId { get; set; }
        public AuthActions Auth { get; set; }

        public string EmpId { get; set; }
        public EmployeeModel Emp { get; set; }

        [DefaultValue(false)]
        public bool V { get; set; }
        [DefaultValue(false)]
        public bool A { get; set; }
        [DefaultValue(false)]
        public bool M { get; set; }
        [DefaultValue(false)]
        public bool R { get; set; }
        [DefaultValue(false)]
        public bool X { get; set; }
    }
}
