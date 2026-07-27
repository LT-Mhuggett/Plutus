using System;

namespace Database.Enums
{
    [Flags]
    public enum ExportLevels
    {
        /// <summary>
        /// Export data that is classed as User Friendly
        /// </summary>
        UserFriendly=1,
        /// <summary>
        /// Export data not clased as User Friendly
        /// </summary>
        NonUserFriendly=2
    };
}
