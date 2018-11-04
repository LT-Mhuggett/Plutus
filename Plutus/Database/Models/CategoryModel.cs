using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Database.Models.Interface;

namespace Database.Models
{
    [Serializable]
    public class CategoryModel : IAuditable, IBase<int>
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public List<ItemModel> Items { get; set; }
        public List<Discount_Category> DisCats { get; set; }
    }
}
