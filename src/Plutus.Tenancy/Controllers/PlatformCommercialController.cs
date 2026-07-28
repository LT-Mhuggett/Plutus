#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Tenancy.Controllers
{
    public sealed record SetContractBody(DateTime RenewalAtUtc, int TermMonths, long PricePenceMonthly, string Notes);

    /// <summary>
    /// WP16.1–16.5 commercial operations (platform-admin, cross-tenant). Reads run with the
    /// platform-admin's Guid.Empty context so the tenant filter's bypass returns every tenant.
    /// Signals/contracts/margin are operator-facing (tenant-identified); analytics is the ONE
    /// anonymised surface — aggregate-only, k-anonymity floored, never a TenantId in the response.
    /// </summary>
    [ApiController]
    public sealed class PlatformCommercialController : ControllerBase
    {
        /// <summary>k-anonymity floor: a metric is suppressed unless at least this many tenants
        /// contribute to it (code-defined, requirements §6).</summary>
        public const int KAnonymityFloor = 3;

        private readonly MySqlDbContext _db;
        private readonly IConfiguration _config;
        public PlatformCommercialController(MySqlDbContext db, IConfiguration config) { _db = db; _config = config; }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        // ── WP16.1 churn signals ──

        /// <summary>Open (uncleared) commercial signals across all tenants — the dashboard feed and
        /// the tenants-list badge source.</summary>
        [HttpGet("api/v1/platform/signals")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Signals() =>
            Ok(await _db.TenantSignals.AsNoTracking().Where(s => s.ClearedAtUtc == null)
                .OrderBy(s => s.TenantId).ThenBy(s => s.Signal)
                .Select(s => new { tenantId = s.TenantId, signal = s.Signal, detail = s.Detail, raisedAtUtc = s.RaisedAtUtc })
                .ToListAsync());

        // ── OP3 subscribers landing: bulk renewals + per-tenant users ──

        /// <summary>All tenant contracts in one call — for the Subscribers list's renewal countdown
        /// + MRR (avoids an N+1 of per-tenant contract fetches).</summary>
        [HttpGet("api/v1/platform/contracts")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Contracts() =>
            Ok(await _db.TenantContracts.AsNoTracking()
                .Select(c => new { c.TenantId, c.RenewalAtUtc, c.PricePenceMonthly, c.TermMonths }).ToListAsync());

        /// <summary>One tenant's users (read-only): the employees with an RBAC assignment in that
        /// tenant + their roles, plus the tenant's last portal-login day (WP13.1 metering is
        /// per-tenant, not per-user, so login recency is reported at tenant level).</summary>
        [HttpGet("api/v1/platform/tenants/{id}/users")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> TenantUsers([FromRoute] Guid id)
        {
            var employees = await _db.Employees.IgnoreQueryFilters().AsNoTracking()
                .Where(e => EF.Property<Guid>(e, "TenantId") == id)
                .Select(e => new { e.Id, e.FName, e.LName, e.Email }).ToListAsync();
            var assignments = await _db.RbacRoleAssignments.IgnoreQueryFilters().AsNoTracking().Include(a => a.Role)
                .Where(a => a.TenantId == id).Select(a => new { a.UserId, Role = a.Role.Name }).ToListAsync();
            var lastPortalDay = await _db.TenantUsageRollups.IgnoreQueryFilters().AsNoTracking()
                .Where(r => r.TenantId == id && r.Metric == UsageMetrics.LoginsPortal && r.Value > 0)
                .Select(r => (DateOnly?)r.BusinessDay).OrderByDescending(d => d).FirstOrDefaultAsync();

            var users = employees.Select(e => new
            {
                id = e.Id, name = $"{e.FName} {e.LName}".Trim(), email = e.Email,
                roles = assignments.Where(a => a.UserId == e.Id).Select(a => a.Role).Distinct().ToArray(),
            }).Where(u => u.roles.Length > 0 || !string.IsNullOrEmpty(u.email)).OrderBy(u => u.name).ToList();

            return Ok(new { lastPortalActivityDay = lastPortalDay?.ToString("yyyy-MM-dd"), users });
        }

        // ── WP16.2 contract / renewal tracking ──

        [HttpGet("api/v1/platform/tenants/{id}/contract")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> GetContract([FromRoute] Guid id)
        {
            var c = await _db.TenantContracts.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == id);
            if (c == null) return NoContent();
            return Ok(new { c.TenantId, c.RenewalAtUtc, c.TermMonths, c.PricePenceMonthly, c.Notes, c.UpdatedAtUtc, c.UpdatedBy });
        }

        /// <summary>Upsert the tenant's relationship record (renewal date, term, negotiated price).
        /// Audited. Billing money-truth stays with the provider once it exists.</summary>
        [HttpPut("api/v1/platform/tenants/{id}/contract")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> SetContract([FromRoute] Guid id, [FromBody] SetContractBody body)
        {
            if (body == null) return BadRequest();
            _db.CurrentUser = Actor.ToString();
            var c = await _db.TenantContracts.FirstOrDefaultAsync(x => x.TenantId == id);
            if (c == null) _db.TenantContracts.Add(c = new TenantContract { TenantId = id });
            c.RenewalAtUtc = body.RenewalAtUtc;
            c.TermMonths = body.TermMonths;
            c.PricePenceMonthly = body.PricePenceMonthly;
            c.Notes = body.Notes;
            c.UpdatedAtUtc = DateTime.UtcNow;
            c.UpdatedBy = Actor.ToString();
            _db.Audit(id, Actor, "contract.set", nameof(TenantContract), id.ToString(),
                new { body.RenewalAtUtc, body.TermMonths, body.PricePenceMonthly });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ── WP16.3 margin view (config-driven, deliberately simple) ──

        /// <summary>Per-tenant margin: revenue from the WP16.2 contract price, cost attributed by the
        /// tenant's share of platform activity (sales.count over the trailing 30 days) against a
        /// config-file infra total, plus any per-tenant direct cost. Shared single box — no
        /// metering-based allocation (revisit if tenants ever get isolated resources). Missing config
        /// yields a clean empty state, never a 500.</summary>
        [HttpGet("api/v1/platform/margin")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Margin()
        {
            var costs = LoadCosts();
            if (costs == null)
                return Ok(new { configured = false, monthlyInfraPence = 0L, tenants = Array.Empty<object>() });

            var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
            var activity = (await _db.TenantUsageRollups.IgnoreQueryFilters().AsNoTracking()
                    .Where(r => r.Metric == UsageMetrics.SalesCount && r.BusinessDay >= since)
                    .GroupBy(r => r.TenantId).Select(g => new { TenantId = g.Key, Sales = g.Sum(x => x.Value) })
                    .ToListAsync())
                .ToDictionary(x => x.TenantId, x => x.Sales);
            long totalActivity = activity.Values.Sum();

            var tenants = await _db.Tenants.AsNoTracking().Where(t => !t.IsSandbox)
                .Select(t => new { t.Id, t.Name, t.PlanId }).ToListAsync();
            var contracts = (await _db.TenantContracts.AsNoTracking().ToListAsync())
                .ToDictionary(c => c.TenantId, c => c.PricePenceMonthly);
            // OP2: revenue = negotiated contract price if set, else the assigned plan's list price, else 0.
            var planPrices = (await _db.SubscriptionPlans.AsNoTracking().ToListAsync())
                .ToDictionary(p => p.Id, p => p.PricePenceMonthly);

            var rows = tenants.Select(t =>
            {
                long act = activity.TryGetValue(t.Id, out var a) ? a : 0;
                double share = totalActivity == 0 ? 0 : (double)act / totalActivity;
                long attributedInfra = (long)Math.Round(costs.MonthlyInfraPence * share);
                long direct = costs.DirectCostsPence != null && costs.DirectCostsPence.TryGetValue(t.Id.ToString(), out var d) ? d : 0;
                long cost = attributedInfra + direct;
                long revenue = contracts.TryGetValue(t.Id, out var r) ? r
                    : (t.PlanId is Guid pid && planPrices.TryGetValue(pid, out var pp) ? pp : 0);
                return new
                {
                    tenantId = t.Id, name = t.Name, activityShare = Math.Round(share, 4),
                    revenuePence = revenue, attributedInfraPence = attributedInfra, directCostPence = direct,
                    costPence = cost, marginPence = revenue - cost,
                };
            }).OrderByDescending(x => x.marginPence).ToList();

            return Ok(new { configured = true, monthlyInfraPence = costs.MonthlyInfraPence, tenants = rows });
        }

        // ── WP16.5 cross-tenant product analytics (anonymised, k-anonymity floored) ──

        /// <summary>Aggregate-only, never per-tenant. Feature adoption + route-group activity + a
        /// coarse login→sale funnel, summed across tenants with a k-anonymity floor so no single
        /// tenant is re-identifiable. Sandbox tenants excluded. No response field carries a TenantId
        /// or tenant name — verified by an automated test.</summary>
        [HttpGet("api/v1/platform/analytics")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Analytics([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var toDay = DateOnly.FromDateTime(to ?? DateTime.UtcNow);
            var fromDay = DateOnly.FromDateTime(from ?? DateTime.UtcNow.AddDays(-30));
            var sandbox = new HashSet<Guid>(await _db.Tenants.AsNoTracking().Where(t => t.IsSandbox).Select(t => t.Id).ToListAsync());

            // Feature adoption + volume, per usage metric, across non-sandbox tenants.
            var cells = (await _db.TenantUsageRollups.IgnoreQueryFilters().AsNoTracking()
                    .Where(r => r.BusinessDay >= fromDay && r.BusinessDay <= toDay && r.Value > 0)
                    .Select(r => new { r.TenantId, r.Metric, r.Value }).ToListAsync())
                .Where(c => !sandbox.Contains(c.TenantId)).ToList();

            var adoption = cells.GroupBy(c => c.Metric).Select(g => new
            {
                metric = g.Key,
                tenantsUsing = g.Select(x => x.TenantId).Distinct().Count(),
                totalUses = g.Sum(x => x.Value),
            })
            .Where(m => m.tenantsUsing >= KAnonymityFloor) // k-anonymity: suppress thin metrics
            .OrderByDescending(m => m.tenantsUsing).ThenBy(m => m.metric).ToList();

            // Route-group activity from WP13.2 request stats (which surfaces get exercised).
            var reqCells = (await _db.TenantRequestStats.AsNoTracking()
                    .Where(r => r.MinuteUtc >= fromDay.ToDateTime(TimeOnly.MinValue) && r.MinuteUtc <= toDay.ToDateTime(TimeOnly.MaxValue))
                    .Select(r => new { r.TenantId, r.RouteGroup, r.Count }).ToListAsync())
                .Where(c => !sandbox.Contains(c.TenantId)).ToList();

            var routes = reqCells.GroupBy(c => c.RouteGroup).Select(g => new
            {
                routeGroup = g.Key,
                tenantsActive = g.Select(x => x.TenantId).Distinct().Count(),
                requests = g.Sum(x => x.Count),
            })
            .Where(r => r.tenantsActive >= KAnonymityFloor)
            .OrderByDescending(r => r.requests).ToList();

            // Coarse funnel: portal logins → sales route activity → completed sales. Each stage carries
            // its contributing-tenant count and is suppressed below the k floor (reported as null).
            long? Floored(long value, int tenants) => tenants >= KAnonymityFloor ? value : (long?)null;
            long loginTenants = cells.Where(c => c.Metric == UsageMetrics.LoginsPortal).Select(c => c.TenantId).Distinct().Count();
            long loginTotal = cells.Where(c => c.Metric == UsageMetrics.LoginsPortal).Sum(c => c.Value);
            var saleRouteTenants = reqCells.Where(c => c.RouteGroup == "sales").Select(c => c.TenantId).Distinct().Count();
            var saleRouteReq = reqCells.Where(c => c.RouteGroup == "sales").Sum(c => c.Count);
            long saleTenants = cells.Where(c => c.Metric == UsageMetrics.SalesCount).Select(c => c.TenantId).Distinct().Count();
            long saleTotal = cells.Where(c => c.Metric == UsageMetrics.SalesCount).Sum(c => c.Value);

            var funnel = new[]
            {
                new { stage = "logins.portal", tenants = (int)loginTenants, value = Floored(loginTotal, (int)loginTenants) },
                new { stage = "sale-started",  tenants = saleRouteTenants,  value = Floored(saleRouteReq, saleRouteTenants) },
                new { stage = "sales.count",   tenants = (int)saleTenants,  value = Floored(saleTotal, (int)saleTenants) },
            };

            return Ok(new
            {
                from = fromDay.ToString("yyyy-MM-dd"), to = toDay.ToString("yyyy-MM-dd"),
                kAnonymityFloor = KAnonymityFloor, adoption, routeGroups = routes, funnel,
            });
        }

        // ── margin config loading ──

        private sealed class CostsConfig
        {
            public long MonthlyInfraPence { get; set; }
            public Dictionary<string, long> DirectCostsPence { get; set; }
        }

        /// <summary>Load platform-costs.json from PLATFORM_COSTS_PATH. Any problem (unset, missing,
        /// unparseable) returns null → the margin endpoint reports an empty, unconfigured state.</summary>
        private CostsConfig LoadCosts()
        {
            try
            {
                var path = _config["PLATFORM_COSTS_PATH"];
                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return null;
                var json = System.IO.File.ReadAllText(path);
                return JsonSerializer.Deserialize<CostsConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { return null; }
        }
    }
}
