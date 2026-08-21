#nullable disable

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.SharedKernel;

namespace Plutus.Tenancy.Controllers
{
    /// <summary>
    /// WP13.2 operator health surface: per-tenant error rate + latency (from TenantRequestStats),
    /// open quarantine depth (from SaleQuarantine) and outbox consumer lag (from ConsumerOffsets).
    /// Platform-admin only — context resolves to <see cref="Guid.Empty"/> so the tenant filter's
    /// bypass returns every tenant (cross-tenant read, exempted by the policy check).
    /// </summary>
    [ApiController]
    public sealed class PlatformHealthController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly TillPresence _presence;

        public PlatformHealthController(MySqlDbContext db, TillPresence presence)
        {
            _db = db;
            _presence = presence;
        }

        /// <summary>One screenful of "is anyone having a bad day?": last-hour per-tenant request
        /// health + open quarantine depth + LIVE TILLS, plus platform-wide consumer lag.</summary>
        [HttpGet("api/v1/platform/health")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Health()
        {
            var since = DateTime.UtcNow.AddHours(-1);

            var stats = await _db.TenantRequestStats.AsNoTracking()
                .Where(x => x.MinuteUtc >= since).ToListAsync();
            var byTenant = stats.GroupBy(s => s.TenantId).Select(g =>
            {
                long count = g.Sum(x => x.Count), e4 = g.Sum(x => x.Err4xx), e5 = g.Sum(x => x.Err5xx);
                return new
                {
                    tenantId = g.Key,
                    requests = count,
                    err4xx = e4,
                    err5xx = e5,
                    errorRatePct = count == 0 ? 0d : Math.Round(e5 * 100.0 / count, 2),
                    peakP95Ms = g.Max(x => x.P95Ms),   // worst minute in the window
                    maxMs = g.Max(x => x.MaxMs),
                };
            }).ToList();

            // Open quarantine depth per tenant (unresolved rows).
            var quarantine = (await _db.SaleQuarantine.AsNoTracking()
                    .Where(q => q.ResolvedAtUtc == null)
                    .GroupBy(q => q.TenantId).Select(g => new { g.Key, C = g.Count() }).ToListAsync())
                .ToDictionary(x => x.Key, x => x.C);

            // ⚠⚠ WP-LIVE (2026-08-21). Matt: *"The 'Live' grey icon needs to be fed from the heart
            // beats. If the tills are active, then the client is active."*
            //
            // ⚠⚠ THE DOT ANSWERED THE WRONG QUESTION. Its only input was `TenantRequestStats`, so it
            // said *"did the API see requests from this tenant in the last hour"* while being read as
            // *"is this customer alive"*. Those differ in both directions, and the awkward one is a
            // trading shop with a **quiet API hour** reading grey next to a plan and a renewal date.
            //
            // ⚠ PRESENCE IS IN-PROCESS AND EPHEMERAL (`TillPresence`) — no query, no write, and no
            // history. It is the freshest signal on the platform and the cheapest to read, which is
            // exactly what a health dot wants.
            var devices = await _db.Devices.AsNoTracking()
                .Select(d => new { d.Id, d.TenantId }).ToListAsync();

            var tenantOfDevice = devices
                .GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First().TenantId);

            var tills = new Dictionary<Guid, (int Online, int Stale, DateTime? LastSeen)>();

            foreach (var entry in _presence.All())
            {
                if (!tenantOfDevice.TryGetValue(entry.DeviceId, out var tenant)) continue;

                tills.TryGetValue(tenant, out var acc);

                // ⚠ ONLINE AND STALE COUNTED APART. "Two tills, one of them stale" is a different
                // morning from "two tills, both fine", and collapsing them into "2 live" hides the
                // shop that is about to ring up.
                if (entry.State == PresenceState.Online) acc.Online++;
                else if (entry.State == PresenceState.Stale) acc.Stale++;

                if (acc.LastSeen is null || entry.LastSeenUtc > acc.LastSeen) acc.LastSeen = entry.LastSeenUtc;

                tills[tenant] = acc;
            }

            // ⚠⚠ THE UNION, AND IT IS THE HALF THAT FIXES THE BUG. Building the list from
            // `TenantRequestStats` alone meant a tenant with no traffic in the window was **not in the
            // response at all** — so the portal had nothing to colour and fell through to grey. A
            // tenant whose till is beating now has a row whether or not the API saw anything else.
            var ids = byTenant.Select(t => t.tenantId).Concat(tills.Keys).Distinct().ToList();

            var tenants = ids.Select(id =>
            {
                var t = byTenant.FirstOrDefault(x => x.tenantId == id);
                tills.TryGetValue(id, out var till);

                return new
                {
                    tenantId = id,
                    requests = t?.requests ?? 0,
                    err4xx = t?.err4xx ?? 0,
                    err5xx = t?.err5xx ?? 0,
                    errorRatePct = t?.errorRatePct ?? 0d,
                    peakP95Ms = t?.peakP95Ms ?? 0,
                    maxMs = t?.maxMs ?? 0,
                    quarantineOpen = quarantine.TryGetValue(id, out var q) ? q : 0,
                    tillsOnline = till.Online,
                    tillsStale = till.Stale,
                    lastTillSeenUtc = till.LastSeen,
                };
            }).OrderByDescending(t => t.err5xx).ThenByDescending(t => t.tillsOnline).ThenBy(t => t.tenantId).ToList();

            // Consumer lag is platform-wide (offsets are per-consumer, not per-tenant).
            long maxOutboxId = await _db.OutboxEvents.AnyAsync() ? await _db.OutboxEvents.MaxAsync(e => e.Id) : 0;
            var consumerLag = (await _db.ConsumerOffsets.AsNoTracking().ToListAsync())
                .Select(o => new { consumer = o.ConsumerName, lag = maxOutboxId - o.LastOutboxId })
                .OrderByDescending(o => o.lag).ToList();

            return Ok(new { generatedAtUtc = DateTime.UtcNow, tenants, consumerLag });
        }

        /// <summary>WP15.2 advisory SLA: monthly availability = minutes whose 5xx-rate is under the
        /// threshold ÷ minutes with traffic, from TenantRequestStats. Single-box, advisory-grade.</summary>
        [HttpGet("api/v1/platform/sla")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Sla([FromQuery] Guid tenantId, [FromQuery] string month)
        {
            // month = "YYYY-MM" (default: current month).
            var now = DateTime.UtcNow;
            int y = now.Year, m = now.Month;
            if (!string.IsNullOrWhiteSpace(month))
            {
                var parts = month.Split('-');
                if (parts.Length != 2 || !int.TryParse(parts[0], out y) || !int.TryParse(parts[1], out m) || m < 1 || m > 12)
                    return BadRequest(new { detail = "month must be YYYY-MM." });
            }
            var start = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddMonths(1);
            const double threshold = 0.01; // 1% 5xx in a minute marks it bad

            var rows = await _db.TenantRequestStats.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.MinuteUtc >= start && x.MinuteUtc < end)
                .Select(x => new { x.MinuteUtc, x.Count, x.Err5xx }).ToListAsync();

            // Collapse route-group rows to one figure per minute, then classify each minute.
            var byMinute = rows.GroupBy(r => r.MinuteUtc)
                .Select(g => new { Count = g.Sum(r => r.Count), Err5xx = g.Sum(r => r.Err5xx) })
                .Where(x => x.Count > 0).ToList();
            var withTraffic = byMinute.Count;
            var good = byMinute.Count(x => (double)x.Err5xx / x.Count < threshold);
            var pct = withTraffic == 0 ? 100.0 : Math.Round(good * 100.0 / withTraffic, 3);

            return Ok(new
            {
                tenantId, month = $"{y:D4}-{m:D2}", thresholdPct = threshold * 100,
                minutesWithTraffic = withTraffic, goodMinutes = good, availabilityPct = pct,
                advisory = true,
            });
        }

        /// <summary>WP17.1 connector health across all tenants — last poll/webhook/outbound, error
        /// streak, and whether the connector is currently silent past its registered window.</summary>
        [HttpGet("api/v1/platform/connectors")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Connectors()
        {
            var now = DateTime.UtcNow;
            var rows = await _db.ConnectorRuns.AsNoTracking().OrderBy(r => r.Connector).ThenBy(r => r.TenantId).ToListAsync();
            return Ok(rows.Select(r => new
            {
                connector = r.Connector, tenantId = r.TenantId,
                lastPollAtUtc = r.LastPollAtUtc, lastWebhookAtUtc = r.LastWebhookAtUtc, lastOutboundAtUtc = r.LastOutboundAtUtc,
                errorStreak = r.ErrorStreak, lastError = r.LastError,
                silent = r.LastActivityAtUtc == null || now - r.LastActivityAtUtc.Value > Plutus.SharedKernel.ConnectorRegistry.SilenceWindow(r.Connector),
            }));
        }

        /// <summary>Drill-down: one tenant's per-minute request-stat rows over a window
        /// (default the last 24h).</summary>
        [HttpGet("api/v1/platform/health/{tenantId}")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> TenantHealth(
            [FromRoute] Guid tenantId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var f = from ?? DateTime.UtcNow.AddHours(-24);
            var t = to ?? DateTime.UtcNow;
            var rows = await _db.TenantRequestStats.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.MinuteUtc >= f && x.MinuteUtc <= t)
                .OrderBy(x => x.MinuteUtc).ThenBy(x => x.RouteGroup)
                .Select(x => new
                {
                    minuteUtc = x.MinuteUtc, routeGroup = x.RouteGroup, count = x.Count,
                    err4xx = x.Err4xx, err5xx = x.Err5xx, p50Ms = x.P50Ms, p95Ms = x.P95Ms, maxMs = x.MaxMs,
                })
                .ToListAsync();
            return Ok(new { tenantId, from = f, to = t, rows });
        }
    }
}
