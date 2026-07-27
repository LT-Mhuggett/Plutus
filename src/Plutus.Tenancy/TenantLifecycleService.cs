using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Tenancy
{
    /// <summary>Tenant lifecycle statuses (mirrors <see cref="Tenant"/>.Status).</summary>
    public static class TenantStatus
    {
        public const byte Trial = 0, Active = 1, PastDue = 2, Suspended = 3, Closed = 4;

        /// <summary>Portal access is refused when Suspended/Closed (D16). Tills keep syncing
        /// regardless — the sales.ingest/device path never consults this.</summary>
        public static bool PortalAllowed(byte status) => status != Suspended && status != Closed;
    }

    /// <summary>
    /// WP10.2/10.4: tenant lifecycle (status + entitlements), offboarding (scheduled deletion) and
    /// the destructive hard-delete. Platform-admin surface; every mutation is audited by the caller.
    /// </summary>
    public sealed class TenantLifecycleService
    {
        public const byte DeletionPending = 0, DeletionCancelled = 1, DeletionExecuted = 2;

        private readonly MySqlDbContext _db;
        public TenantLifecycleService(MySqlDbContext db) => _db = db;

        public Task<Tenant> FindAsync(Guid tenantId, CancellationToken ct = default) =>
            _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);

        /// <summary>Whether a portal login for this tenant is allowed right now (WP10.2).</summary>
        public async Task<bool> PortalAccessAllowedAsync(Guid tenantId, CancellationToken ct = default)
        {
            if (tenantId == Guid.Empty) return true;
            var status = await _db.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId).Select(t => (byte?)t.Status).FirstOrDefaultAsync(ct);
            return status == null || TenantStatus.PortalAllowed(status.Value);
        }

        public async Task SetStatusAsync(Guid tenantId, byte status, string actor, CancellationToken ct = default)
        {
            var tenant = await FindAsync(tenantId, ct) ?? throw new EnrolmentException(404, "Tenant not found.");
            _db.CurrentUser = actor;
            tenant.Status = status;
            await _db.SaveChangesAsync(ct);
        }

        public async Task SetEntitlementsAsync(Guid tenantId, string[] entitlements, string plan, string actor, CancellationToken ct = default)
        {
            var tenant = await FindAsync(tenantId, ct) ?? throw new EnrolmentException(404, "Tenant not found.");
            _db.CurrentUser = actor;
            tenant.Entitlements = JsonSerializer.Serialize(entitlements ?? Array.Empty<string>());
            if (!string.IsNullOrWhiteSpace(plan)) tenant.Plan = plan;
            await _db.SaveChangesAsync(ct);
        }

        /// <summary>Apply a billing webhook's change (entitlements/plan/status) in one save.</summary>
        public async Task ApplyBillingChangeAsync(BillingChange change, string actor, CancellationToken ct = default)
        {
            var tenant = await FindAsync(change.TenantId, ct) ?? throw new EnrolmentException(404, "Tenant not found.");
            _db.CurrentUser = actor;
            tenant.Entitlements = JsonSerializer.Serialize(change.Entitlements ?? Array.Empty<string>());
            if (!string.IsNullOrWhiteSpace(change.Plan)) tenant.Plan = change.Plan;
            if (change.Status.HasValue) tenant.Status = change.Status.Value;
            await _db.SaveChangesAsync(ct);
        }

        public async Task<DeletionSchedule> RequestDeletionAsync(Guid tenantId, int graceDays, Guid actor, CancellationToken ct = default)
        {
            if (tenantId == WellKnownTenants.Kapow) throw new EnrolmentException(400, "The founding tenant cannot be deleted.");
            if (await FindAsync(tenantId, ct) == null) throw new EnrolmentException(404, "Tenant not found.");
            if (await _db.DeletionSchedules.AnyAsync(d => d.TenantId == tenantId && d.Status == DeletionPending, ct))
                throw new EnrolmentException(409, "A deletion is already scheduled for this tenant.");

            var schedule = new DeletionSchedule
            {
                Id = Uuid7.New(), TenantId = tenantId, RequestedAtUtc = DateTime.UtcNow,
                ExecuteAfterUtc = DateTime.UtcNow.AddDays(Math.Max(0, graceDays)),
                Status = DeletionPending, RequestedBy = actor,
            };
            _db.DeletionSchedules.Add(schedule);
            // Closing the tenant locks the portal during the grace window (tills still sync).
            var tenant = await FindAsync(tenantId, ct);
            tenant.Status = TenantStatus.Closed;
            _db.CurrentUser = actor.ToString();
            await _db.SaveChangesAsync(ct);
            return schedule;
        }

        public async Task CancelDeletionAsync(Guid scheduleId, string actor, CancellationToken ct = default)
        {
            var schedule = await _db.DeletionSchedules.FirstOrDefaultAsync(d => d.Id == scheduleId, ct)
                ?? throw new EnrolmentException(404, "Deletion schedule not found.");
            if (schedule.Status != DeletionPending) throw new EnrolmentException(409, "Only a pending deletion can be cancelled.");
            _db.CurrentUser = actor;
            schedule.Status = DeletionCancelled;
            await _db.SaveChangesAsync(ct);
        }

        /// <summary>
        /// Hard-delete every row belonging to a tenant. Discovers tenant tables dynamically from
        /// information_schema (any table with a TenantId column), so it stays correct as the schema
        /// grows. FK checks are suspended for the batch since the WHOLE tenant is removed at once.
        /// The founding Kapow tenant is refused. Returns the number of tables cleared.
        /// </summary>
        public async Task<int> HardDeleteTenantAsync(Guid tenantId, CancellationToken ct = default)
        {
            if (tenantId == Guid.Empty || tenantId == WellKnownTenants.Kapow)
                throw new InvalidOperationException("Refusing to hard-delete the founding/unscoped tenant.");

            var conn = _db.Database.GetDbConnection();
            var tables = new List<string>();
            await using (var listCmd = conn.CreateCommand())
            {
                listCmd.CommandText =
                    "SELECT table_name FROM information_schema.columns " +
                    "WHERE table_schema = DATABASE() AND column_name = 'TenantId' " +
                    "AND table_name NOT IN ('DeletionSchedules','Tenants')";
                if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
                await using var reader = await listCmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) tables.Add(reader.GetString(0));
            }

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS=0;", ct);
            foreach (var table in tables)
                await _db.Database.ExecuteSqlRawAsync($"DELETE FROM `{table}` WHERE `TenantId` = {{0}};", new object[] { tenantId.ToString() }, ct);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM `Tenants` WHERE `Id` = {0};", new object[] { tenantId.ToString() }, ct);
            await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS=1;", ct);
            await tx.CommitAsync(ct);
            return tables.Count;
        }

        /// <summary>WP10.4 retention purge (safe, ephemeral only): used/expired enrolment codes older
        /// than <paramref name="days"/>. Sales are NEVER touched here (6-year legal retention).
        /// Heartbeat/telemetry purge lands when a telemetry table exists.</summary>
        public async Task<int> PurgeExpiredEnrolmentCodesAsync(int days, CancellationToken ct = default)
        {
            var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, days));
            return await _db.EnrolmentCodes
                .Where(c => c.CreatedAtUtc < cutoff && (c.UsedAtUtc != null || c.ExpiresAtUtc < DateTime.UtcNow))
                .ExecuteDeleteAsync(ct);
        }
    }
}
