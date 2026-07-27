using System;
using System.Collections.Generic;
using System.Text;

namespace Database.Models
{
    public interface IAuditable
    {
        DateTime Created { get; set; }
        DateTime Modified { get; set; }
        string CreatedBy { get; set; }
        string ModifiedBy { get; set; }
    }
}
