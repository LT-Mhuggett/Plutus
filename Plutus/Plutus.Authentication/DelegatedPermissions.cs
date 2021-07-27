using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Plutus.Authentication
{
    public static class DelegatedPermissions
    {
        public const string ReadThings = "Things.Read";
        public const string ReadOtherThings = "OtherThings.Read";
        public const string WritePermission = "Permission.Write";

        public static string[] All => typeof(Plutus.Authentication.DelegatedPermissions)
            .GetFields()
            .Where(f => f.Name != nameof(All))
            .Select(f => f.GetValue(null) as string)
            .ToArray();
    }
}
