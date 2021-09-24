using Plutus.Entities.Attributes;
using Plutus.Entities.Enums;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class PaymentMethod : Base<int>, IPaymentMethod
    {
        #region Properties
        [Exportable]
        [Required]
        public string Name { get; set; }
        [Exportable(ExportLevels.NonUserFriendly)]
        [Required]
        public decimal Charge { get; set; }
        [Exportable(ExportLevels.NonUserFriendly)]
        [Required]
        public decimal MinimumCharge { get; set; }
        [Exportable(ExportLevels.NonUserFriendly)]
        [Required]
        public bool IsChangeable { get; set; }
        [Exportable(ExportLevels.NonUserFriendly)]
        [Required]
        public bool IsCashBackable { get; set; }

        #region Relationships
        #region Collections
        public virtual ICollection<PaymentMethod_Sale> PaySales { get; set; }
        #endregion
        #endregion
        #endregion
    }
}
