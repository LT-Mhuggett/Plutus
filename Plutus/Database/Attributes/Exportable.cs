using Database.Enums;
using System;

namespace Database.Attributes
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class Exportable : Attribute
    {
        #region Fields
        #endregion

        #region Property
        public ExportLevels ExportLevels { get; }
        #endregion

        public Exportable(ExportLevels exportLevels = ExportLevels.UserFriendly)
        {
            ExportLevels = exportLevels;
        }
    }
}
