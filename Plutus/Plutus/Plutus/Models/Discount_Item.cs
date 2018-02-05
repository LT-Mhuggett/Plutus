using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Plutus.Models.Interface;

namespace Plutus.Models
{
    public class Discount_Item : IAuditable, IBase<int>
    {
        public int Id { get; set; }
        public ItemModel Item { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }

        [DisplayFormat(ApplyFormatInEditMode = true, DataFormatString = "{0:yyyy-MM-dd}")]
        public DiscountModel Discount { get; set; }

        [NotMapped]
        public string FullDateTime => string.Format("{0} - {1}", StartDateTime, EndDateTime);
    }
}
