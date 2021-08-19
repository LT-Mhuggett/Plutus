using Plutus.Entities.Enums;
using System;

namespace Plutus.Entities.Attributes
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class Exportable : Attribute
    {
        #region Property
        public ExportLevels ExportLevels { get; }
        #endregion

        public Exportable(ExportLevels exportLevels = ExportLevels.UserFriendly)
        {
            ExportLevels = exportLevels;
        }
    }
}
