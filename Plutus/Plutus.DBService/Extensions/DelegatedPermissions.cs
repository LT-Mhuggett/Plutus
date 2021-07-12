using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Plutus.DBService.Extensions
{
    internal static class DelegatedPermissions
    {
        public const string ReadThings = "Things.Read";
        public const string ReadOtherThings = "OtherThings.Read";

        public static string[] All => typeof(DelegatedPermissions)
            .GetFields()
            .Where(f => f.Name != nameof(All))
            .Select(f => f.GetValue(null) as string)
            .ToArray();
    }
}
