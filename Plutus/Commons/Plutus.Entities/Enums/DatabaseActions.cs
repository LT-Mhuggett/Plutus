using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plutus.Entities.Enums
{
    public enum DatabaseActions
    {
        /// <summary>
        /// Entity has been deleted
        /// </summary>
        Deleted = 1,

        /// <summary>
        /// Entity has been modified
        /// </summary>
        Modified = 2,

        /// <summary>
        /// Entity has been created
        /// </summary>
        Created = 3
    }
}
