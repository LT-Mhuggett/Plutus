using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;
using System.Text;
using Database.Models.Interface;

namespace Database.Models
{
    public class DiscountModel : IAuditable, IBase<int>
    {
        public int Id { get; set; }
        [Column("Name")]
        public string Name { get; set; }
        public List<Discount_Category> DisCategoryList { get; set; }
        public List<Discount_Item> DisItemList { get; set; }
        /**
         * Type = 0 = Fixed $$$ off
         * 
         * Type = 1 = % off
         */
        public int Type { get; set; }
        public decimal Amount { get; set; }
        public int UsesPerTransaction { get; set; }
        public int RequiredNumOfItems { get; set; }

        [NotMapped]
        [DefaultValue(false)]
        public bool Changed { get; set; }
        
    }
}
