using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace Database.Models.Interface
{
    public interface IBase<T>
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        T Id { get; set; }
    }
}
