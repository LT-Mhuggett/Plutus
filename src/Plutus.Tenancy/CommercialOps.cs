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
    /// WP16.1/16.2 churn + renewal thresholds — code-defined (tuned later), pure and unit-tested at
    /// the boundaries. Kept separate from the DB sweep so the arithmetic can be tested without a
    /// database.
    /// </summary>
    public static class ChurnThresholds
    {
        public const int LookbackDays = 28;         // window length for the usage comparison
        public const double DeclineFraction = 0.30; // "declining" = current < prior * (1 - this)
        public const int GoneQuietDays = 14;        // no portal login for this many days
        public const int SupportHeavyTickets28d = 5; // OP4: this many tickets in 28 days = support-heavy
        public static readonly int[] RenewalDueDays = { 60, 30, 7 }; // renewal-due thresholds

        /// <summary>Sales fell by MORE than 30% vs the prior equal window. Needs a prior baseline
        /// (prior &gt; 0) so a brand-new tenant with no history isn't flagged.</summary>
        public static bool IsDeclining(long current, long prior) =>
            prior > 0 && current < prior * (1.0 - DeclineFraction);

        /// <summary>Silent for MORE than the window (strictly &gt;, so exactly 14 days is still OK).</summary>
        public static bool IsGoneQuiet(int daysSinceLastLogin) => daysSinceLastLogin > GoneQuietDays;

        /// <summary>At or above the ticket threshold over the trailing 28 days (OP4).</summary>
        public static bool IsSupportHeavy(int tickets28d) => tickets28d >= SupportHeavyTickets28d;

        /// <summary>The largest threshold the renewal has crossed (60/30/7), or null if further out /
        /// already past. Drives one "renewal-due" signal that escalates as the date nears.</summary>
        public static int? RenewalThresholdCrossed(int daysUntilRenewal)
        {
            if (daysUntilRenewal < 0) return null;
            foreach (var t in RenewalDueDays.OrderBy(x => x)) // 7, 30, 60
                if (daysUntilRenewal <= t) return t;
            return null;
        }
    }

    /// <summary>
    /// WP16.1 churn-signal sweep (a RetentionSweeper pass). Cross-tenant: reads WP13.1 usage rollups
    /// (unscoped, so it spans every tenant) and raises/clears TenantSignals + a keyed operator alert
    /// per signal — exactly the WP13.3 "raise once, clear on recovery" pattern. Sandbox tenants are
    /// skipped. OP4 closed the `support-heavy` seam: it now fires on ticket volume (≥5 in 28 days).
    /// </summary>
    public static class ChurnSweep
    {
        public static string AlertKey(Guid tenantId, string signal) => $"signal:{signal}:{tenantId}";

        public static async Task EvaluateAsync(MySqlDbContext db, IOperatorAlerter alerter, DateTime nowUtc, CancellationToken ct = default)
        {
            var today = DateOnly.FromDateTime(nowUtc);
            var windowStart = today.AddDays(-2 * ChurnThresholds.LookbackDays); // 56 days back
            var mid = today.AddDays(-ChurnThresholds.LookbackDays);

            // Live tenants only (sandbox excluded; Closed tenants aren't worth churn-flagging).
            var tenants = await db.Tenants.AsNoTracking()
                .Where(t => !t.IsSandbox && t.Status != 4)
                .Select(t => t.Id).ToListAsync(ct);

            // One pass over the relevant rollup cells for the whole window.
            var cells = await db.TenantUsageRollups.IgnoreQueryFilters().AsNoTracking()
                .Where(r => r.BusinessDay >= windowStart && r.BusinessDay < today
                    && (r.Metric == UsageMetrics.SalesCount || r.Metric == UsageMetrics.LoginsPortal))
                .Select(r => new { r.TenantId, r.BusinessDay, r.Metric, r.Value })
                .ToListAsync(ct);

            // OP4: tickets raised in the trailing 28 days, per tenant (support-heavy input).
            var ticketCutoff = nowUtc.AddDays(-ChurnThresholds.LookbackDays);
            var ticketCounts = (await db.SupportTickets.IgnoreQueryFilters().AsNoTracking()
                    .Where(t => t.CreatedAtUtc >= ticketCutoff)
                    .GroupBy(t => t.TenantId).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct))
                .ToDictionary(x => x.Key, x => x.C);

            foreach (var tenantId in tenants)
            {
                var mine = cells.Where(c => c.TenantId == tenantId).ToList();

                // usage-declining
                long current = mine.Where(c => c.Metric == UsageMetrics.SalesCount && c.BusinessDay >= mid).Sum(c => c.Value);
                long prior = mine.Where(c => c.Metric == UsageMetrics.SalesCount && c.BusinessDay < mid).Sum(c => c.Value);
                if (ChurnThresholds.IsDeclining(current, prior))
                    await RaiseAsync(db, alerter, tenantId, TenantSignals.UsageDeclining,
                        $"28-day sales {current} vs prior {prior} (down >{ChurnThresholds.DeclineFraction:P0}).", nowUtc, ct);
                else
                    await ClearAsync(db, alerter, tenantId, TenantSignals.UsageDeclining, ct);

                // gone-quiet (only if the tenant has ever logged in — no baseline ⇒ no signal)
                var lastLogin = mine.Where(c => c.Metric == UsageMetrics.LoginsPortal && c.Value > 0)
                    .Select(c => (DateOnly?)c.BusinessDay).Max();
                if (lastLogin is DateOnly d && ChurnThresholds.IsGoneQuiet(today.DayNumber - d.DayNumber))
                    await RaiseAsync(db, alerter, tenantId, TenantSignals.GoneQuiet,
                        $"No portal login since {d:yyyy-MM-dd} ({today.DayNumber - d.DayNumber} days).", nowUtc, ct);
                else
                    await ClearAsync(db, alerter, tenantId, TenantSignals.GoneQuiet, ct);

                // support-heavy (OP4): ticket volume over the trailing 28 days
                var tickets = ticketCounts.TryGetValue(tenantId, out var tc) ? tc : 0;
                if (ChurnThresholds.IsSupportHeavy(tickets))
                    await RaiseAsync(db, alerter, tenantId, TenantSignals.SupportHeavy,
                        $"{tickets} support tickets in the last {ChurnThresholds.LookbackDays} days.", nowUtc, ct);
                else
                    await ClearAsync(db, alerter, tenantId, TenantSignals.SupportHeavy, ct);
            }

            await db.SaveChangesAsync(ct);
        }

        private static async Task RaiseAsync(MySqlDbContext db, IOperatorAlerter alerter, Guid tenantId, string signal, string detail, DateTime nowUtc, CancellationToken ct)
        {
            await TenantSignalStore.RaiseAsync(db, tenantId, signal, detail, nowUtc, ct);
            await alerter.RaiseAsync(AlertKey(tenantId, signal), signal, tenantId, "signal", detail, ct);
        }

        private static async Task ClearAsync(MySqlDbContext db, IOperatorAlerter alerter, Guid tenantId, string signal, CancellationToken ct)
        {
            await TenantSignalStore.ClearAsync(db, tenantId, signal, DateTime.UtcNow, ct);
            await alerter.ClearAsync(AlertKey(tenantId, signal), ct);
        }
    }

    /// <summary>
    /// WP18.2 compliance sweep (a RetentionSweeper pass): raise a "dpa-missing" signal (+ keyed
    /// alert) for every live, non-sandbox tenant with no signed DPA on record; clear it once signed.
    /// Makes the compliance gap visible on the dashboard exactly like the churn signals.
    /// </summary>
    public static class ComplianceSweep
    {
        public static async Task EvaluateAsync(MySqlDbContext db, IOperatorAlerter alerter, DateTime nowUtc, CancellationToken ct = default)
        {
            var tenants = await db.Tenants.AsNoTracking()
                .Where(t => !t.IsSandbox && t.Status != 4)
                .Select(t => new { t.Id, t.DpaSignedAtUtc }).ToListAsync(ct);
            foreach (var t in tenants)
            {
                var key = ChurnSweep.AlertKey(t.Id, TenantSignals.DpaMissing);
                if (t.DpaSignedAtUtc == null)
                {
                    await TenantSignalStore.RaiseAsync(db, t.Id, TenantSignals.DpaMissing, "No signed DPA on record.", nowUtc, ct);
                    await alerter.RaiseAsync(key, TenantSignals.DpaMissing, t.Id, "signal", "No signed DPA on record.", ct);
                }
                else
                {
                    await TenantSignalStore.ClearAsync(db, t.Id, TenantSignals.DpaMissing, nowUtc, ct);
                    await alerter.ClearAsync(key, ct);
                }
            }
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// WP16.2 renewal-due sweep (a RetentionSweeper pass): for every tenant with a contract, raise a
    /// single "renewal-due" signal (+ keyed alert) as the renewal crosses 60/30/7 days out, clearing
    /// it once past. Uses the ChurnSweep raise/clear helpers so it shares the one signal table.
    /// </summary>
    public static class RenewalSweep
    {
        public static async Task EvaluateAsync(MySqlDbContext db, IOperatorAlerter alerter, DateTime nowUtc, CancellationToken ct = default)
        {
            var contracts = await db.TenantContracts.AsNoTracking().ToListAsync(ct);
            var sandbox = new HashSet<Guid>(await db.Tenants.AsNoTracking().Where(t => t.IsSandbox).Select(t => t.Id).ToListAsync(ct));

            foreach (var c in contracts)
            {
                if (sandbox.Contains(c.TenantId)) continue;
                var days = (int)Math.Floor((c.RenewalAtUtc - nowUtc).TotalDays);
                var crossed = ChurnThresholds.RenewalThresholdCrossed(days);
                var key = ChurnSweep.AlertKey(c.TenantId, TenantSignals.RenewalDue);
                if (crossed is int threshold)
                {
                    var detail = $"Renewal on {c.RenewalAtUtc:yyyy-MM-dd} — {Math.Max(days, 0)} day(s) out (≤{threshold}).";
                    await TenantSignalStore.RaiseAsync(db, c.TenantId, TenantSignals.RenewalDue, detail, nowUtc, ct);
                    await alerter.RaiseAsync(key, TenantSignals.RenewalDue, c.TenantId, "signal", detail, ct);
                }
                else
                {
                    await TenantSignalStore.ClearAsync(db, c.TenantId, TenantSignals.RenewalDue, nowUtc, ct);
                    await alerter.ClearAsync(key, ct);
                }
            }
            await db.SaveChangesAsync(ct);
        }
    }
}
