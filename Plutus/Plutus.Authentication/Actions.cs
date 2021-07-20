using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Plutus.Authentication
{
    /// <summary>
    /// Actions that can be done on the API
    /// </summary>
    public static class Actions
    {
        public const string ReadThings = "Things/Read";
        public const string ReadOtherThings = "OtherThings/Read";

        public static string[] All => typeof(Actions)
            .GetFields()
            .Where(f => f.Name != nameof(All))
            .Select(f => f.GetValue(null) as string)
            .ToArray();
    }
}
