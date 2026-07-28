#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.Reporting;
using Plutus.SharedKernel;
using Plutus.Tenancy;

namespace Plutus.DBService.Controllers
{
    /// <summary>
    /// WP14.3 sandbox / demo tenants: flip the IsSandbox flag and RESET a sandbox to a deterministic
    /// demo state. Reset is hard-guarded to sandbox tenants — it truncates transactional rows and
    /// re-seeds a fixed catalogue + 30 fixed sales, then rebuilds the projections, so a sandbox
    /// always returns to the same row counts + penny totals. Platform-admin, audited.
    /// </summary>
    [ApiController]
    public sealed class SandboxController : ControllerBase
    {
        public sealed record SetSandboxBody(bool IsSandbox);

        private readonly MySqlDbContext _db;
        private readonly DbContextOptions<MySqlDbContext> _dbOptions;

        public SandboxController(MySqlDbContext db, DbContextOptions<MySqlDbContext> dbOptions)
        {
            _db = db;
            _dbOptions = dbOptions;
        }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        [HttpPut("api/v1/platform/tenants/{id}/sandbox")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SetSandbox([FromRoute] Guid id, [FromBody] SetSandboxBody body)
        {
            var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id);
            if (tenant == null) return NotFound();
            _db.CurrentUser = Actor.ToString();
            tenant.IsSandbox = body?.IsSandbox ?? false;
            _db.Audit(id, Actor, "tenant.sandbox.set", "Tenant", id.ToString(), new { tenant.IsSandbox });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("api/v1/platform/tenants/{id}/reset")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Reset([FromRoute] Guid id, CancellationToken ct)
        {
            var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
            if (tenant == null) return NotFound();
            if (!tenant.IsSandbox)
                return Conflict(new { detail = "Reset is only allowed on a sandbox tenant." }); // hard guard

            // A tenant-scoped context so the seeded spine's SHADOW TenantId stamps correctly and
            // reads/deletes auto-filter to this tenant. Disposed before the audit write.
            int sales; long gross;
            await using (var db = new MySqlDbContext(_dbOptions, new FixedTenantContext(id)) { CurrentUser = "sandbox-reset" })
                (sales, gross) = await SandboxSeeder.ResetAsync(db, id, ct);

            _db.CurrentUser = Actor.ToString();
            _db.Audit(id, Actor, "tenant.reset", "Tenant", id.ToString(), new { sales, grossPence = gross });
            await _db.SaveChangesAsync();
            return Ok(new { tenantId = id, sales, grossPence = gross });
        }
    }

    /// <summary>Deterministic demo seed: exactly 30 identical £5.00 sales re-created on every reset,
    /// so row counts and penny totals are invariant. Seeds the v1 sales graph only (SaleV2 + lines +
    /// tenders) — no legacy spine, which avoids its FK web and works on both SQLite and MySQL; the
    /// rollup rebuild buckets them honestly. Runs on a TENANT-SCOPED context.</summary>
    internal static class SandboxSeeder
    {
        private const int SaleCount = 30;
        private const long SaleGrossPence = 500;
        private const string Sku = "DEMO-0001";

        public static async Task<(int Sales, long GrossPence)> ResetAsync(MySqlDbContext db, Guid tenantId, CancellationToken ct)
        {
            // 1. truncate transactional rows (context is tenant-scoped → filtered).
            db.SaleLines.RemoveRange(await db.SaleLines.ToListAsync(ct));
            db.SaleTenders.RemoveRange(await db.SaleTenders.ToListAsync(ct));
            db.SaleAdjustments.RemoveRange(await db.SaleAdjustments.ToListAsync(ct));
            db.SalesV2.RemoveRange(await db.SalesV2.ToListAsync(ct));
            db.SalesRollups.RemoveRange(await db.SalesRollups.ToListAsync(ct));
            db.VatRollups.RemoveRange(await db.VatRollups.ToListAsync(ct));
            db.TenantUsageRollups.RemoveRange(await db.TenantUsageRollups.ToListAsync(ct));
            await db.SaveChangesAsync(ct);

            // 2. seed exactly 30 identical £5.00 zero-rated cash sales on 30 consecutive days,
            // through a stable virtual till + item id (deterministic per tenant).
            var tillId = DeterministicGuid.ForName("sandbox-till", tenantId.ToString());
            var itemId = DeterministicGuid.ForName("sandbox-item", Sku);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            long total = 0;
            for (var i = 0; i < SaleCount; i++)
            {
                var saleId = Uuid7.New();
                var line = new SaleLine
                {
                    Id = Uuid7.New(), TenantId = tenantId, SaleId = saleId, LineNo = 1,
                    ItemId = itemId, ItemIdOne = Sku, Qty = 1,
                    UnitPricePence = SaleGrossPence, LineGrossPence = SaleGrossPence, DiscountPence = 0,
                    VatRateBp = 0, VatAmountPence = 0,
                };
                var tender = new SaleTender
                {
                    Id = Uuid7.New(), TenantId = tenantId, SaleId = saleId,
                    TenderType = TenderType.Cash, AmountPence = SaleGrossPence, ChangePence = 0,
                };
                db.SalesV2.Add(SaleV2.Create(saleId, tenantId, tillId, Uuid7.New(), i + 1, SaleChannel.Till,
                    today.AddDays(-i), DateTime.UtcNow, DateTime.UtcNow, SaleGrossPence, 0, new[] { line }, new[] { tender }));
                total += SaleGrossPence;
            }
            await db.SaveChangesAsync(ct);

            // 3. rebuild projections honestly from the seeded sales.
            await RollupRebuilder.RebuildAsync(db, tenantId, ct);
            await UsageRebuilder.RebuildAsync(db, tenantId, ct);

            return (SaleCount, total);
        }
    }
}
