using Plutus.Entities.Attributes;
using Plutus.Entities.Enums;
using Plutus.Entities.Models.Interface;
using System;

namespace Plutus.Entities.Models
{
    public class Auditable : IAuditable
    {
        #region Properties
        [Exportable(ExportLevels.NonUserFriendly)]
        public DateTime CreatedAt { get; set; }

        [Exportable(ExportLevels.NonUserFriendly)]
        public DateTime ModifiedAt { get; set; }

        [Exportable(ExportLevels.NonUserFriendly)]
        public string CreatedBy { get; set; }

        [Exportable(ExportLevels.NonUserFriendly)]
        public string ModifiedBy { get; set; }
        #endregion

        public Auditable()
        {
        }

        public Auditable(IAuditable auditable)
        {
            CreatedAt = auditable.CreatedAt;
            CreatedBy = auditable.CreatedBy;
            ModifiedAt = auditable.ModifiedAt;
            ModifiedBy = auditable.ModifiedBy;
        }
    }
}
