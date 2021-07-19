using Plutus.Entities.Attributes;
using Plutus.Entities.Enums;
using Plutus.Entities.Models.Interface;
using System;

namespace Plutus.Entities.Models
{
    [Serializable]
    public class AuthActionAPIMapping : CompositeBase<string, int>, IAuthActionAPIMapping
    {
        #region Properties
        [Exportable(ExportLevels.NonUserFriendly)]
        public string Module { get; set; }

        #region Relationships
        [Exportable]
        public virtual Role Role { get; set; }

        public virtual AuthActions AuthAction { get; set; }

        #region Collections

        #endregion
        #endregion
        #endregion
    }
}
