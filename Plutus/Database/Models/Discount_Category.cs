using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Database.Models.Interface;

namespace Database.Models
{
    public class Discount_Category : IAuditable, IBase<int>
    {
        public int Id { get; set; }
        public CategoryModel Cat { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }

        [DisplayFormat(ApplyFormatInEditMode = true, DataFormatString = "{0:yyyy-MM-dd}")]
        public DiscountModel Discount { get; set; }

        [NotMapped]
        public string FullDateTime => string.Format("{0} - {1}", StartDateTime, EndDateTime);
    }
}
