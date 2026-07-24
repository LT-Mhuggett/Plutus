using Plutus.Entities.Attributes;
using Plutus.Entities.Enums;
using Plutus.Entities.Models.Interface;
using System.ComponentModel.DataAnnotations;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Business : Base<Guid>, IBusiness
    {
        #region Properties


        [Exportable(ExportLevels.NonUserFriendly)]
        public byte[]? Logo { get; set; }

        [Exportable]
        [Required]
        public string Name { get; set; }

        [Exportable]
        [Required]
        public string NameAbbr { get; set; }

        [Exportable]
        public decimal? RecMarkup { get; set; }

        [Exportable]
        public string VatIN { get; set; }
        #region Relationships
        #region Collections
        public virtual ICollection<Category> Categories { get; set; }
        public virtual ICollection<Discount> Discounts { get; set; }
        public virtual ICollection<Employee> Employees { get; set; }
        public virtual ICollection<Item> Items { get; set; }
        public virtual ICollection<Role> Roles { get; set; }
        public virtual ICollection<Store> Stores { get; set; }
        public virtual ICollection<Tax> Taxes { get; set; }
        #endregion 
        #endregion
        #endregion
    }
}
