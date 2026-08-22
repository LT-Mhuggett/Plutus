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

        /// <summary>
        /// Find-or-create the (tenant, day, metric) cell.
        ///
        /// ⚠⚠ THE `Local` LOOKUP COMES FIRST, AND IT IS NOT AN OPTIMISATION — fixed 2026-08-22.
        /// `FirstOrDefaultAsync` runs SQL, and a cell this same SaveChanges has already CREATED has
        /// no row yet — so the query returns null, the code adds a second instance with the same key,
        /// and EF throws *"another instance with the same key value for {TenantId, BusinessDay,
        /// Metric} is already being tracked"*.
        ///
        /// ⚠ IT KILLED `RequestStatsFlusher` ON EVERY CYCLE. That flusher folds one snapshot per
        /// (tenant, ROUTE GROUP) into a single `api.requests` cell, so the second route group a
        /// tenant touched in any minute hit this — which is every minute a tenant is awake. The
        /// whole minute was then lost, request stats included, and the failure only showed as a
        /// logged exception because the flusher catches and continues.
        ///
        /// ⚠ Any caller folding several deltas into ONE cell before saving has the same shape. The
        /// sales consumer never hit it only because its two calls use two different metrics.
        /// </summary>
        private static async Task<TenantUsageRollup> Cell(MySqlDbContext db, Guid tenantId, DateOnly day, string metric, CancellationToken ct)
        {
            // ⚠ Already tracked (added or loaded) in this context? Then that instance IS the cell —
            // going to the database for it would miss an unsaved one and duplicate it.
            var row = db.TenantUsageRollups.Local
                .FirstOrDefault(x => x.TenantId == tenantId && x.BusinessDay == day && x.Metric == metric);
            row ??= await db.TenantUsageRollups.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.BusinessDay == day && x.Metric == metric, ct);
            if (row == null)
                db.TenantUsageRollups.Add(row = new TenantUsageRollup { TenantId = tenantId, BusinessDay = day, Metric = metric });
            return row;
        }
    }
}
