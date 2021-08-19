using System;

namespace Plutus.Entities.Enums
{
    [Flags]
    public enum ExportLevels
    {
        /// <summary>
        /// Export data that is classed as User Friendly
        /// </summary>
        UserFriendly = 1,

        /// <summary>
        /// Export data that is classed as Non User Friendly
        /// </summary>
        NonUserFriendly = 2
    }
}
