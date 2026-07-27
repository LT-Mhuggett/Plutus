using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// WP13.1 operator usage metering: one cell per (TenantId, BusinessDay, Metric) → Value.
    /// The cross-tenant read surface the operator dashboard sums over. Per-tenant rows are folded
    /// from three feeds — SaleRecorded (event-fed, sales.*), login hooks (logins.*), and a nightly
    /// counted-metrics sweep (stores/tills/users.active, storage.*). Tenant-owned (global filter),
    /// so platform-admin reads (Guid.Empty context) see every tenant via the filter's bypass branch.
    /// </summary>
    public class TenantUsageRollup
    {
        public Guid TenantId { get; set; }
        public DateOnly BusinessDay { get; set; }
        public string Metric { get; set; }
        public long Value { get; set; }
    }

    /// <summary>The v1 metric catalogue — code-defined (like the permission catalogue), not stored.</summary>
    public static class UsageMetrics
    {
        public const string SalesCount = "sales.count";
        public const string SalesGrossPence = "sales.grossPence";
        public const string ApiRequests = "api.requests";           // fed by WP13.2 middleware
        public const string LoginsPortal = "logins.portal";
        public const string LoginsTill = "logins.till";
        public const string StoresActive = "stores.active";
        public const string TillsActive = "tills.active";
        public const string UsersActive = "users.active";
        public const string StorageRowsSalesV2 = "storage.rowsSalesV2";
    }

    /// <summary>
    /// Fold helper shared by every feed. Reads bypass the tenant filter and writes set TenantId
    /// explicitly (mirrors the rollup projection) so it is correct whatever the ambient context —
    /// but callers writing for a tenant OTHER than the ambient one must use an unscoped context
    /// (Guid.Empty) or StampAndGuardTenant will block the cross-tenant write. The helper never
    /// calls SaveChanges: the outbox drainer saves for the consumer; other callers save their own.
    /// </summary>
    public static class UsageMeter
    {
        /// <summary>Add <paramref name="delta"/> to the (tenant, day, metric) cell (find-or-create).</summary>
        public static async Task AddAsync(MySqlDbContext db, Guid tenantId, DateOnly day, string metric, long delta, CancellationToken ct = default)
        {
            var row = await Cell(db, tenantId, day, metric, ct);
            row.Value += delta;
        }

        /// <summary>Set the cell to an absolute <paramref name="value"/> — for counted metrics
        /// (stores/tills/users.active, storage.*) recomputed wholesale each nightly sweep.</summary>
        public static async Task SetAsync(MySqlDbContext db, Guid tenantId, DateOnly day, string metric, long value, CancellationToken ct = default)
        {
            var row = await Cell(db, tenantId, day, metric, ct);
            row.Value = value;
        }

        private static async Task<TenantUsageRollup> Cell(MySqlDbContext db, Guid tenantId, DateOnly day, string metric, CancellationToken ct)
        {
            var row = await db.TenantUsageRollups.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.BusinessDay == day && x.Metric == metric, ct);
            if (row == null)
                db.TenantUsageRollups.Add(row = new TenantUsageRollup { TenantId = tenantId, BusinessDay = day, Metric = metric });
            return row;
        }
    }
}
