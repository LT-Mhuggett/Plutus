using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.SharedKernel;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// WP16.1 commercial-health signal. GLOBAL (TenantId as data) — the churn sweep writes signals
    /// for every tenant from an unscoped context, so it can't be tenant-owned. Keyed by
    /// (TenantId, Signal); raising is idempotent (upsert) and clearing stamps ClearedAtUtc, exactly
    /// like OperatorAlerts. An open signal (ClearedAtUtc == null) is what the dashboard shows.
    /// </summary>
    public class TenantSignal
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Signal { get; set; }
        public string Detail { get; set; }
        public DateTime RaisedAtUtc { get; set; }
        public DateTime LastSeenAtUtc { get; set; }
        public DateTime? ClearedAtUtc { get; set; }
    }

    /// <summary>Code-defined signal catalogue (like the permission/metric catalogues).</summary>
    public static class TenantSignals
    {
        public const string UsageDeclining = "usage-declining"; // 28-day sales down >30% vs prior 28
        public const string GoneQuiet = "gone-quiet";           // no portal login for 14 days
        public const string SupportHeavy = "support-heavy";     // seam only — no ticket source yet
        public const string RenewalDue = "renewal-due";         // WP16.2 — renewal within 60/30/7 days
        public const string DpaMissing = "dpa-missing";         // WP18.2 — no signed DPA on record
    }

    /// <summary>Keyed upsert for TenantSignals (global table), mirroring OperatorAlertStore: raising
    /// the same (tenant, signal) updates one open row (LastSeen), re-opens a cleared one, and never
    /// creates duplicates. Caller saves.</summary>
    public static class TenantSignalStore
    {
        public static async Task RaiseAsync(MySqlDbContext db, Guid tenantId, string signal, string detail, DateTime nowUtc, CancellationToken ct = default)
        {
            var row = await db.TenantSignals.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Signal == signal, ct);
            if (row == null)
            {
                db.TenantSignals.Add(new TenantSignal
                {
                    Id = Uuid7.New(), TenantId = tenantId, Signal = signal, Detail = detail,
                    RaisedAtUtc = nowUtc, LastSeenAtUtc = nowUtc, ClearedAtUtc = null,
                });
                return;
            }
            row.LastSeenAtUtc = nowUtc;
            row.Detail = detail;
            if (row.ClearedAtUtc != null) { row.ClearedAtUtc = null; row.RaisedAtUtc = nowUtc; } // re-raise after recovery
        }

        public static async Task ClearAsync(MySqlDbContext db, Guid tenantId, string signal, DateTime nowUtc, CancellationToken ct = default)
        {
            var row = await db.TenantSignals.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Signal == signal && s.ClearedAtUtc == null, ct);
            if (row != null) row.ClearedAtUtc = nowUtc;
        }
    }

    /// <summary>
    /// OP2 subscription plan — the operator's named price list (e.g. "Standard £99/mo"). GLOBAL,
    /// operator-managed. Assigning a plan to a tenant copies its Name into <c>Tenant.Plan</c> and
    /// its entitlement bundle into <c>Tenant.Entitlements</c> (so every existing entitlement read
    /// keeps working); a <see cref="TenantContract"/> price, if present, is the negotiated override
    /// that wins over the plan's list price for margin/billing.
    /// </summary>
    public class SubscriptionPlan
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public long PricePenceMonthly { get; set; }
        public string EntitlementsJson { get; set; }   // JSON array, same vocabulary as Tenant.Entitlements
        public bool Active { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public string UpdatedBy { get; set; }
    }

    /// <summary>
    /// WP16.2 contract / renewal record — deliberately thin. The billing provider owns money truth
    /// once it exists; this tracks the *relationship* (negotiated monthly price, term, renewal date)
    /// that no provider webhook carries. GLOBAL, operator-managed, one row per tenant (TenantId is
    /// the key), audited on edit.
    /// </summary>
    public class TenantContract
    {
        public Guid TenantId { get; set; }
        public DateTime RenewalAtUtc { get; set; }
        public int TermMonths { get; set; }
        public long PricePenceMonthly { get; set; }
        public string Notes { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public string UpdatedBy { get; set; }
    }
}
