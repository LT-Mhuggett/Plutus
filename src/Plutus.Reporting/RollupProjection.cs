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

namespace Plutus.Reporting
{
    /// <summary>
    /// WP3.3: folds each SaleRecorded into the SalesRollups/VatRollups tables — the
    /// projection the report endpoints read. Same drainer contract as the legacy bridge
    /// (shared scoped context, no SaveChanges here, all reads before the first mutation):
    /// ProcessedEvents dedupe makes each increment effectively-once. ALL recorded sales
    /// project, including LegacyRef (ETL) rows — though those normally arrive via
    /// <see cref="RollupRebuilder"/> because the migrator writes no outbox events.
    /// </summary>
    public sealed class RollupProjectionConsumer : IEventConsumer
    {
        public const string ConsumerName = "reporting-rollups";
        private readonly MySqlDbContext _db;

        public RollupProjectionConsumer(MySqlDbContext db) => _db = db;

        public string Name => ConsumerName;

        public async Task HandleAsync(DomainEvent e, CancellationToken ct)
        {
            if (e is not SaleRecorded recorded) return;

            var sale = await _db.SalesV2.IgnoreQueryFilters().AsNoTracking()
                .Include(s => s.Lines)
                .FirstOrDefaultAsync(s => s.Id == recorded.SaleId, ct);
            if (sale == null)
                throw new InvalidOperationException($"SaleRecorded {recorded.SaleId}: SaleV2 row not found.");

            var (companyId, storeId) = await ResolveSpineAsync(_db, sale.TillId, ct);

            // WP3.4 period lock: a sale that ARRIVES after its period closed posts to the
            // first day after it (the next open period) — a published year never silently
            // changes. Sales received before the close belong to their real day.
            var closedPeriods = await ClosedPeriodsAsync(_db, sale.TenantId, ct);
            var day = EffectiveDay(sale.BusinessDay, sale.ReceivedAtUtc, closedPeriods);

            var salesRollup = await _db.SalesRollups.IgnoreQueryFilters().FirstOrDefaultAsync(
                r => r.TenantId == sale.TenantId && r.TillId == sale.TillId && r.BusinessDay == day, ct);

            // WP2c-exempt: the grain is (rate, BAND), not rate alone. Two bands can share a rate —
            // zero-rated and exempt are both 0% — and merging them destroys the partial-exemption
            // figure permanently.
            var vatByBand = VatByRateAndBand(sale.Lines);
            var vatRollups = new Dictionary<(int, string), VatRollup>();
            foreach (var (rate, band) in vatByBand.Keys)
            {
                var row = await _db.VatRollups.IgnoreQueryFilters().FirstOrDefaultAsync(
                    r => r.TenantId == sale.TenantId && r.StoreId == storeId &&
                         r.BusinessDay == day && r.VatRateBp == rate && r.VatBand == band, ct);
                if (row != null) vatRollups[(rate, band)] = row;
            }

            // ---- mutations (nothing below throws) ----
            if (day != sale.BusinessDay)
                _db.AuditLogs.Add(new AuditLog
                {
                    TenantId = sale.TenantId, ActorUserId = sale.OperatorUserId ?? Guid.Empty,
                    Action = "period.late-post", EntityType = "SaleV2", EntityId = sale.Id.ToString(),
                    DetailJson = $"{{\"businessDay\":\"{sale.BusinessDay:yyyy-MM-dd}\",\"postedTo\":\"{day:yyyy-MM-dd}\"}}",
                    AtUtc = DateTime.UtcNow,
                });

            if (salesRollup == null)
                _db.SalesRollups.Add(salesRollup = new SalesRollup
                {
                    TenantId = sale.TenantId, CompanyId = companyId, StoreId = storeId,
                    TillId = sale.TillId, BusinessDay = day,
                });
            salesRollup.GrossPence += sale.GrossPence;
            salesRollup.VatPence += sale.VatPence;
            salesRollup.TxnCount += 1;

            foreach (var (key, sums) in vatByBand)
            {
                if (!vatRollups.TryGetValue(key, out var row))
                    _db.VatRollups.Add(row = new VatRollup
                    {
                        TenantId = sale.TenantId, CompanyId = companyId, StoreId = storeId,
                        BusinessDay = day, VatRateBp = key.Rate, VatBand = key.Band,
                    });
                row.GrossPence += sums.Gross;
                row.NetPence += sums.Gross - sums.Vat;
                row.VatPence += sums.Vat;
            }
        }

        internal static Task<List<FinancialPeriod>> ClosedPeriodsAsync(MySqlDbContext db, Guid tenantId, CancellationToken ct) =>
            db.FinancialPeriods.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.TenantId == tenantId && p.Status == PeriodStatus.Closed)
                .ToListAsync(ct);

        /// <summary>Walks forward past every closed period containing the day (adjacent closed
        /// periods chain until the first open day). ONLY sales received after the covering
        /// period's close redirect — pre-close trade stays on its real day, which is what
        /// makes a rebuild reproduce the locked figures.</summary>
        internal static DateOnly EffectiveDay(
            DateOnly businessDay, DateTime receivedAtUtc, IReadOnlyList<FinancialPeriod> closedPeriods)
        {
            var day = businessDay;
            for (var guard = 0; guard < 100; guard++)
            {
                var covering = closedPeriods.FirstOrDefault(p =>
                    p.StartDay <= day && day <= p.EndDay &&
                    p.ClosedAtUtc.HasValue && receivedAtUtc > p.ClosedAtUtc.Value);
                if (covering == null) return day;
                day = covering.EndDay.AddDays(1);
            }
            return day;
        }

        /// <summary>
        /// Takings per (rate, band). ⚠ The BAND is part of the key on purpose: zero-rated and exempt
        /// lines both carry `VatRateBp = 0`, so grouping on the rate alone merges two legally
        /// different kinds of supply into one bucket — and once merged, the partial-exemption figure
        /// cannot be recovered without replaying every sale line.
        ///
        /// A null band (a till that doesn't send one, or any line recorded before WP2c-exempt) stays
        /// null and groups with its own kind; reports fall back to snapping the rate for those.
        /// </summary>
        /// <remarks>Public so the grain is unit-testable: merging two 0% bands is a silent,
        /// unrecoverable data loss, which is exactly the kind of thing a test has to hold.</remarks>
        public static Dictionary<(int Rate, string Band), (long Gross, long Vat)> VatByRateAndBand(
            IEnumerable<SaleLine> lines) =>
            lines.GroupBy(l => (Rate: l.VatRateBp, Band: l.VatBand))
                .ToDictionary(g => g.Key, g => (g.Sum(l => l.LineGrossPence), g.Sum(l => l.VatAmountPence)));

        /// <summary>Till → store → company via the legacy hierarchy; unknown tills bucket
        /// under store 0 / the tenant's first company so no sale is ever dropped.</summary>
        internal static async Task<(Guid CompanyId, int StoreId)> ResolveSpineAsync(
            MySqlDbContext db, Guid tillId, CancellationToken ct)
        {
            var till = await db.Till.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tillId, ct);
            if (till == null)
                return (await db.Business.IgnoreQueryFilters().AsNoTracking()
                    .Select(b => b.Id).FirstOrDefaultAsync(ct), 0);
            var store = await db.Stores.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == till.StoreId, ct);
            return (store?.BusinessId ?? await db.Business.IgnoreQueryFilters().AsNoTracking()
                .Select(b => b.Id).FirstOrDefaultAsync(ct), till.StoreId);
        }
    }

    /// <summary>
    /// WP3.3 rebuild: wipe the tenant's rollups and re-aggregate straight from SalesV2 —
    /// used after a projection bug, and to fold in migrated (LegacyRef) rows that never had
    /// outbox events. Runs in ONE transaction whose snapshot covers both the outbox high-water
    /// mark and the table scan; the consumer offset is advanced to that mark so already-scanned
    /// sales are not re-applied by the dispatcher afterwards (rebuild == incremental).
    /// </summary>
    public static class RollupRebuilder
    {
        public static async Task<(int SalesRollups, int VatRollups, int SalesScanned)> RebuildAsync(
            MySqlDbContext db, Guid tenantId, CancellationToken ct = default)
        {
            db.CurrentUser = "rollup-rebuild";
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            long outboxMark = await db.OutboxEvents.AnyAsync(ct) ? await db.OutboxEvents.MaxAsync(e => e.Id, ct) : 0;

            var sales = await db.SalesV2.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.TenantId == tenantId)
                .Include(s => s.Lines)
                .ToListAsync(ct);

            // spine cache: till → (company, store)
            var spine = new Dictionary<Guid, (Guid CompanyId, int StoreId)>();
            foreach (var tillId in sales.Select(s => s.TillId).Distinct())
                spine[tillId] = await RollupProjectionConsumer.ResolveSpineAsync(db, tillId, ct);

            // WP3.4: rebuild applies the SAME closed-period redirect as the incremental
            // consumer — rebuild after a close must reproduce the locked figures exactly.
            var closedPeriods = await RollupProjectionConsumer.ClosedPeriodsAsync(db, tenantId, ct);
            var effectiveDay = new Func<SaleV2, DateOnly>(
                s => RollupProjectionConsumer.EffectiveDay(s.BusinessDay, s.ReceivedAtUtc, closedPeriods));

            var oldSales = await db.SalesRollups.IgnoreQueryFilters()
                .Where(r => r.TenantId == tenantId).ToListAsync(ct);
            var oldVat = await db.VatRollups.IgnoreQueryFilters()
                .Where(r => r.TenantId == tenantId).ToListAsync(ct);
            db.SalesRollups.RemoveRange(oldSales);
            db.VatRollups.RemoveRange(oldVat);

            var salesRollups = sales
                .GroupBy(s => (s.TillId, Day: effectiveDay(s)))
                .Select(g => new SalesRollup
                {
                    TenantId = tenantId,
                    CompanyId = spine[g.Key.TillId].CompanyId,
                    StoreId = spine[g.Key.TillId].StoreId,
                    TillId = g.Key.TillId,
                    BusinessDay = g.Key.Day,
                    GrossPence = g.Sum(s => s.GrossPence),
                    VatPence = g.Sum(s => s.VatPence),
                    TxnCount = g.Count(),
                })
                .ToList();
            db.SalesRollups.AddRange(salesRollups);

            var vatRollups = sales
                .SelectMany(s => s.Lines.Select(l => (Sale: s, Line: l)))
                // WP2c-exempt: band is part of the grain — see VatByRateAndBand.
                .GroupBy(x => (spine[x.Sale.TillId].StoreId, Day: effectiveDay(x.Sale), x.Line.VatRateBp, x.Line.VatBand))
                .Select(g => new VatRollup
                {
                    TenantId = tenantId,
                    CompanyId = spine[g.First().Sale.TillId].CompanyId,
                    StoreId = g.Key.StoreId,
                    BusinessDay = g.Key.Day,
                    VatRateBp = g.Key.VatRateBp,
                    VatBand = g.Key.VatBand,
                    GrossPence = g.Sum(x => x.Line.LineGrossPence),
                    NetPence = g.Sum(x => x.Line.LineGrossPence - x.Line.VatAmountPence),
                    VatPence = g.Sum(x => x.Line.VatAmountPence),
                })
                .ToList();
            db.VatRollups.AddRange(vatRollups);

            // Advance the consumer offset to the snapshot's high-water mark so the dispatcher
            // does not re-apply events for sales this scan already counted.
            var offset = await db.ConsumerOffsets
                .FirstOrDefaultAsync(o => o.ConsumerName == RollupProjectionConsumer.ConsumerName, ct);
            if (offset == null)
                db.ConsumerOffsets.Add(new ConsumerOffset
                {
                    ConsumerName = RollupProjectionConsumer.ConsumerName,
                    LastOutboxId = outboxMark, UpdatedAtUtc = DateTime.UtcNow,
                });
            else if (offset.LastOutboxId < outboxMark)
            {
                offset.LastOutboxId = outboxMark;
                offset.UpdatedAtUtc = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return (salesRollups.Count, vatRollups.Count, sales.Count);
        }
    }
}
