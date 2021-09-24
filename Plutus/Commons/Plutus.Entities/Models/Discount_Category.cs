using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Discount_Category : Base<int>, IDiscount_Category
    {
        #region Properties
        [Exportable]
        public DateTime StartDateTime { get; set; }
        [Exportable]
        public DateTime EndDateTime { get; set; }

        [NotMapped]
        public string FullDateTime => string.Format("{0} - {1}", StartDateTime, EndDateTime);

        #region Relationships 
        public virtual Category Cat { get; set; }
        public virtual Discount Discount { get; set; }
        #endregion
        #endregion
    }
}
