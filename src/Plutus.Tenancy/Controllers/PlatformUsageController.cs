#nullable disable

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Tenancy.Controllers
{
    /// <summary>
    /// WP13.1 operator usage metering — the read surface for the operator dashboard. All routes
    /// are platform-admin: their context resolves to <see cref="Guid.Empty"/>, so the tenant query
    /// filter's bypass branch returns rows for EVERY tenant without IgnoreQueryFilters. This is a
    /// deliberate cross-tenant read, exempted in the isolation suite by the platform-admin policy
    /// check (never by skipping the endpoint).
    /// </summary>
    [ApiController]
    public sealed class PlatformUsageController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        public PlatformUsageController(MySqlDbContext db) => _db = db;

        /// <summary>Raw usage cells, filterable by tenant / date range / metric.</summary>
        [HttpGet("api/v1/platform/usage")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Usage(
            [FromQuery] Guid? tenantId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string metric)
        {
            var q = _db.TenantUsageRollups.AsNoTracking(); // platform-admin ctx = Empty → all tenants
            if (tenantId is Guid tid) q = q.Where(x => x.TenantId == tid);
            if (from is DateOnly f) q = q.Where(x => x.BusinessDay >= f);
            if (to is DateOnly t) q = q.Where(x => x.BusinessDay <= t);
            if (!string.IsNullOrWhiteSpace(metric)) q = q.Where(x => x.Metric == metric);

            return Ok(await q
                .OrderBy(x => x.TenantId).ThenBy(x => x.BusinessDay).ThenBy(x => x.Metric)
                .Select(x => new { tenantId = x.TenantId, businessDay = x.BusinessDay, metric = x.Metric, value = x.Value })
                .ToListAsync());
        }

        /// <summary>The dashboard's one-call feed: last 30 days, per tenant — metric totals plus a
        /// daily sales.count series for the sparkline.</summary>
        [HttpGet("api/v1/platform/usage/summary")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Summary([FromQuery] bool includeSandbox = false)
        {
            var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30);
            var rows = await _db.TenantUsageRollups.AsNoTracking()
                .Where(x => x.BusinessDay >= since).ToListAsync();

            // WP14.3: sandbox/demo tenants are excluded from commercial usage by default.
            if (!includeSandbox)
            {
                var sandbox = await _db.Tenants.AsNoTracking().Where(t => t.IsSandbox).Select(t => t.Id).ToListAsync();
                if (sandbox.Count > 0) rows = rows.Where(r => !sandbox.Contains(r.TenantId)).ToList();
            }

            var byTenant = rows.GroupBy(r => r.TenantId).Select(g => new
            {
                tenantId = g.Key,
                totals = g.GroupBy(r => r.Metric).ToDictionary(m => m.Key, m => m.Sum(r => r.Value)),
                salesDaily = g.Where(r => r.Metric == UsageMetrics.SalesCount)
                    .OrderBy(r => r.BusinessDay)
                    .Select(r => new { day = r.BusinessDay, value = r.Value }).ToList(),
            }).OrderBy(t => t.tenantId).ToList();

            return Ok(byTenant);
        }

        /// <summary>Rebuild a tenant's sales.* cells from SalesV2 (rebuild == incremental).</summary>
        [HttpPost("api/v1/platform/usage/rebuild")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Rebuild([FromQuery] Guid tenantId)
        {
            if (tenantId == Guid.Empty) return BadRequest(new { detail = "tenantId is required." });
            var days = await UsageRebuilder.RebuildAsync(_db, tenantId);
            return Ok(new { tenantId, daysRebuilt = days });
        }
    }
}
