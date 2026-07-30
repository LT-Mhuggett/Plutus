using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Customers
{
    /// <summary>
    /// FE1 one-off data lift: before the tier catalogue, a membership carried a free-typed tier
    /// name + rate. This turns each distinct (tenant, tier name) already in use into a
    /// <see cref="LoyaltyTier"/> row and points its memberships at it, so existing data becomes
    /// catalogue-managed with no operator effort.
    ///
    /// IDEMPOTENT — safe to run on every boot: it only creates tiers whose (tenant, name) has no
    /// row yet, and only sets <c>TierId</c> where it is still null. Runs after Migrate() rather
    /// than as migration SQL so it is plain testable C# (and so UUIDv7 ids stay the convention
    /// instead of MySQL's UUID()).
    /// </summary>
    public static class LoyaltyTierBackfill
    {
        /// <returns>(tiers created, memberships linked)</returns>
        public static async Task<(int TiersCreated, int MembershipsLinked)> ApplyAsync(
            MySqlDbContext db, CancellationToken ct = default)
        {
            // IgnoreQueryFilters: this is a cross-tenant maintenance pass, not a request.
            var orphans = await db.Memberships.IgnoreQueryFilters()
                .Where(m => m.TierId == null)
                .ToListAsync(ct);
            if (orphans.Count == 0) return (0, 0);

            // The context refuses to save without an actor (audit stamp) — this pass has no
            // request principal, so it names itself.
            if (string.IsNullOrEmpty(db.CurrentUser)) db.CurrentUser = "loyalty-tier-backfill";

            var existing = await db.LoyaltyTiers.IgnoreQueryFilters().ToListAsync(ct);
            var created = 0;

            // Group by tenant + name (case-insensitive, matching the unique index's collation).
            foreach (var group in orphans
                .Where(m => !string.IsNullOrWhiteSpace(m.Tier))
                .GroupBy(m => new TenantName(m.TenantId, m.Tier.Trim()), TenantNameComparer.Instance))
            {
                var name = group.Key.Name;
                var tier = existing.FirstOrDefault(t => t.TenantId == group.Key.TenantId
                    && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

                if (tier == null)
                {
                    // Rate: the most common rate seen under that name wins (a name with two rates
                    // in the wild is a data mistake the catalogue is meant to end — the outliers
                    // keep their as-assigned snapshot columns, so nobody's discount changes today).
                    var rate = group.GroupBy(m => m.AutoDiscountRate)
                        .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key)
                        .First().Key;
                    tier = new LoyaltyTier
                    {
                        Id = Uuid7.New(), TenantId = group.Key.TenantId, Name = name,
                        AutoDiscountRate = rate, DurationMonths = 12, Active = true,
                        SortOrder = 0, CreatedAtUtc = DateTime.UtcNow,
                    };
                    db.LoyaltyTiers.Add(tier);
                    existing.Add(tier);
                    created++;
                }

                foreach (var m in group) m.TierId = tier.Id;
            }

            var linked = orphans.Count(m => m.TierId != null);
            if (created > 0 || linked > 0) await db.SaveChangesAsync(ct);
            return (created, linked);
        }

        private readonly record struct TenantName(Guid TenantId, string Name);

        private sealed class TenantNameComparer : System.Collections.Generic.IEqualityComparer<TenantName>
        {
            public static readonly TenantNameComparer Instance = new();
            public bool Equals(TenantName a, TenantName b) =>
                a.TenantId == b.TenantId && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            public int GetHashCode(TenantName x) =>
                HashCode.Combine(x.TenantId, x.Name?.ToLowerInvariant());
        }
    }
}
