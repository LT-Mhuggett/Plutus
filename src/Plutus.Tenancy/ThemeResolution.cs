#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Plutus.Entities.Models;

namespace Plutus.Tenancy
{
    /// <summary>
    /// FE10: which theme does a till actually get? Pure — the controller loads the (small,
    /// tenant-scoped) assignment table and this decides. Precedence, most specific first:
    /// Till > Group > Store > Tenant > built-in default. A till in SEVERAL themed groups takes
    /// the most recently (re)assigned one — deterministic, and "the last thing an admin did
    /// wins" is the least surprising rule when two groups fight over a till.
    /// </summary>
    public static class ThemeResolution
    {
        public const byte ScopeTenant = 0;
        public const byte ScopeStore = 1;
        public const byte ScopeGroup = 2;
        public const byte ScopeTill = 3;

        /// <summary>The three assignable built-ins. "system" follows the device's light/dark
        /// setting (the pre-FE10 behaviour); light/dark force a mode with the stock palette.</summary>
        public static readonly IReadOnlySet<string> BuiltIns = new HashSet<string>(StringComparer.Ordinal)
        {
            "builtin:system", "builtin:light", "builtin:dark",
        };

        /// <summary>Canonical ScopeKey text: "" tenant · int store · lower "D" guid group/till.</summary>
        public static string KeyFor(Guid id) => id.ToString("D").ToLowerInvariant();

        public static (TillThemeAssignment? Picked, string Source) Pick(
            IReadOnlyList<TillThemeAssignment> assignments,
            Guid? tillId,
            int? storeId,
            IReadOnlyCollection<Guid> groupIds)
        {
            if (tillId is Guid t)
            {
                var key = KeyFor(t);
                var a = assignments.FirstOrDefault(x => x.Scope == ScopeTill && string.Equals(x.ScopeKey, key, StringComparison.OrdinalIgnoreCase));
                if (a != null) return (a, "till");
            }
            if (groupIds is { Count: > 0 })
            {
                var keys = groupIds.Select(KeyFor).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var a = assignments
                    .Where(x => x.Scope == ScopeGroup && keys.Contains(x.ScopeKey))
                    .OrderByDescending(x => x.UpdatedAtUtc)
                    .FirstOrDefault();
                if (a != null) return (a, "group");
            }
            if (storeId is int s)
            {
                var key = s.ToString();
                var a = assignments.FirstOrDefault(x => x.Scope == ScopeStore && x.ScopeKey == key);
                if (a != null) return (a, "store");
            }
            var tenant = assignments.FirstOrDefault(x => x.Scope == ScopeTenant);
            return tenant != null ? (tenant, "tenant") : (null, "default");
        }
    }
}
