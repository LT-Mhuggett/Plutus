#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Tenancy
{
    /// <summary>
    /// WP13.1 event-fed metering: folds each SaleRecorded into the tenant's sales.count and
    /// sales.grossPence cells for the sale's business day. Same drainer contract as the rollup
    /// projection (own consumer name → own offset + ProcessedEvents dedupe = effectively-once;
    /// no SaveChanges here — the drainer commits the fold atomically with the offset). Sets
    /// TenantId via the fold helper's explicit-write path, so it is correct whatever the drainer's
    /// ambient tenant (Kapow on this single-tenant host).
    /// </summary>
    public sealed class UsageMeteringConsumer : IEventConsumer
    {
        public const string ConsumerName = "usage-metering";
        private readonly MySqlDbContext _db;

        public UsageMeteringConsumer(MySqlDbContext db) => _db = db;

        public string Name => ConsumerName;

        public async Task HandleAsync(DomainEvent e, CancellationToken ct)
        {
            if (e is not SaleRecorded recorded) return;

            var sale = await _db.SalesV2.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == recorded.SaleId, ct);
            if (sale == null)
                throw new InvalidOperationException($"SaleRecorded {recorded.SaleId}: SaleV2 row not found.");

            await UsageMeter.AddAsync(_db, sale.TenantId, sale.BusinessDay, UsageMetrics.SalesCount, 1, ct);
            await UsageMeter.AddAsync(_db, sale.TenantId, sale.BusinessDay, UsageMetrics.SalesGrossPence, sale.GrossPence, ct);
        }
    }

    /// <summary>
    /// WP13.1 rebuild: re-derives a tenant's sales.* cells straight from SalesV2 (the WP3.3
    /// rollup-rebuild pattern). Upsert-and-prune rather than wipe-and-add — the composite natural
    /// key can't have a Deleted and Added instance with the same key in one SaveChanges. Runs in
    /// ONE transaction and advances the consumer offset to the outbox high-water mark so the
    /// dispatcher doesn't re-apply already-counted sales: rebuild == incremental.
    /// </summary>
    public static class UsageRebuilder
    {
        public static async Task<int> RebuildAsync(MySqlDbContext db, Guid tenantId, CancellationToken ct = default)
        {
            db.CurrentUser = "usage-rebuild";
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            long outboxMark = await db.OutboxEvents.AnyAsync(ct) ? await db.OutboxEvents.MaxAsync(e => e.Id, ct) : 0;

            var existing = await db.TenantUsageRollups.IgnoreQueryFilters()
                .Where(r => r.TenantId == tenantId &&
                            (r.Metric == UsageMetrics.SalesCount || r.Metric == UsageMetrics.SalesGrossPence))
                .ToListAsync(ct);
            var byKey = existing.ToDictionary(r => (r.BusinessDay, r.Metric));

            var perDay = await db.SalesV2.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.TenantId == tenantId)
                .GroupBy(s => s.BusinessDay)
                .Select(g => new { Day = g.Key, Count = (long)g.Count(), Gross = g.Sum(s => s.GrossPence) })
                .ToListAsync(ct);

            var seen = new HashSet<(DateOnly, string)>();
            void Upsert(DateOnly day, string metric, long value)
            {
                seen.Add((day, metric));
                if (byKey.TryGetValue((day, metric), out var row)) row.Value = value;
                else db.TenantUsageRollups.Add(new TenantUsageRollup
                { TenantId = tenantId, BusinessDay = day, Metric = metric, Value = value });
            }

            foreach (var d in perDay)
            {
                Upsert(d.Day, UsageMetrics.SalesCount, d.Count);
                Upsert(d.Day, UsageMetrics.SalesGrossPence, d.Gross);
            }
            // prune stale sales.* cells no longer produced (e.g. a day whose sales were all voided).
            foreach (var r in existing)
                if (!seen.Contains((r.BusinessDay, r.Metric))) db.TenantUsageRollups.Remove(r);

            var offset = await db.ConsumerOffsets.FirstOrDefaultAsync(o => o.ConsumerName == UsageMeteringConsumer.ConsumerName, ct);
            if (offset == null)
                db.ConsumerOffsets.Add(new ConsumerOffset
                { ConsumerName = UsageMeteringConsumer.ConsumerName, LastOutboxId = outboxMark, UpdatedAtUtc = DateTime.UtcNow });
            else if (offset.LastOutboxId < outboxMark)
            {
                offset.LastOutboxId = outboxMark;
                offset.UpdatedAtUtc = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return perDay.Count;
        }
    }
}
