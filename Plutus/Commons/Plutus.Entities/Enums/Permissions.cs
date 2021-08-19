using System;

namespace Plutus.Entities.Enums
{
    [Flags]
    public enum Permissions
    {
        None    =   0,
        Read    =   1,
        Execute =   1<<1,
        Write   =   1<<2,
        Delete  =   1<<3
    };
}