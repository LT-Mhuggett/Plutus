using Plutus.Entities.Attributes;
using Plutus.Entities.Models.Interface;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class Discount_Item : Base<int>, IDiscount_Item
    {
        #region Properties
        [Exportable]
        public DateTime StartDateTime { get; set; }
        [Exportable]
        public DateTime EndDateTime { get; set; }
        [NotMapped]
        public string FullDateTime => string.Format("{0} - {1}", StartDateTime, EndDateTime);

        #region Relationships 
        public virtual Item Item { get; set; }
        public virtual Discount Discount { get; set; }
        #endregion
        #endregion
    }
}
