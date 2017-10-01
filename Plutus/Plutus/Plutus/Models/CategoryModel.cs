using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Plutus.Models.Interface;

namespace Plutus.Models
{
    public class CategoryModel : IAuditable, IBase<int>
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public List<ItemModel> Items { get; set; }
    }
}
