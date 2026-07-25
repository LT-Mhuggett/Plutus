#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Cash
{
    /// <summary>
    /// WP7.2 endpoints: cash-event ingest (device or pos.sell — the same principals that
    /// submit sales) and the portal banking view (expected vs counted vs variance per
    /// till/day, cash + card takings side by side).
    /// </summary>
    [ApiController]
    public sealed class CashController : ControllerBase
    {
        private readonly CashEventService _service;
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;

        public CashController(CashEventService service, MySqlDbContext db, ITenantContext tenant)
        {
            _service = service;
            _db = db;
            _tenant = tenant;
        }

        [HttpPost("api/v1/cash-events")]
        [Authorize(Policy = PlutusPolicies.SalesIngest)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Ingest([FromBody] CashEventRequest body)
        {
            if (body == null) return BadRequest(new { detail = "Body is required." });

            var tokenDid = _tenant.DeviceId;
            if (tokenDid.HasValue && body.DeviceId != Guid.Empty && body.DeviceId != tokenDid.Value)
                return StatusCode(403, new { detail = "Body deviceId does not match the token device." });
            var deviceId = tokenDid ?? body.DeviceId;
            var acting = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "device";

            var outcome = await _service.IngestAsync(body, _tenant.TenantId, deviceId, acting);
            return StatusCode(outcome.Status, outcome.Body);
        }

        /// <summary>Session events for a till/day (the X/Z drill).</summary>
        [HttpGet("api/v1/cash-events")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalFinancialsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> List([FromQuery] Guid tillId, [FromQuery] DateOnly day) =>
            Ok(await _db.CashEvents.AsNoTracking()
                .Where(e => e.TillId == tillId && e.BusinessDay == day)
                .OrderBy(e => e.OccurredAtUtc)
                .Select(e => new
                {
                    eventId = e.Id, type = e.Type.ToString(), amountPence = e.AmountPence,
                    countedPence = e.CountedPence, expectedPence = e.ExpectedPence,
                    variancePence = e.VariancePence, reason = e.Reason,
                    occurredAtUtc = e.OccurredAtUtc, operatorUserId = e.OperatorUserId,
                })
                .ToListAsync());

        /// <summary>Banking view (architecture §9.2): per till per day — float/ins/outs,
        /// cash + card takings, expected vs counted vs variance, Z state.</summary>
        [HttpGet("api/v1/cash/banking")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalFinancialsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Banking([FromQuery] DateOnly from, [FromQuery] DateOnly to)
        {
            if (to < from) return BadRequest(new { detail = "to must be >= from." });

            var events = await _db.CashEvents.AsNoTracking()
                .Where(e => e.BusinessDay >= from && e.BusinessDay <= to)
                .ToListAsync();

            var tenders = await (
                from t in _db.SaleTenders.AsNoTracking()
                join s in _db.SalesV2.AsNoTracking() on t.SaleId equals s.Id
                where s.BusinessDay >= @from && s.BusinessDay <= to
                select new { s.TillId, s.BusinessDay, t.TenderType, Net = t.AmountPence - t.ChangePence })
                .ToListAsync();

            var keys = events.Select(e => (e.TillId, e.BusinessDay))
                .Concat(tenders.Select(t => (t.TillId, t.BusinessDay)))
                .Distinct()
                .OrderByDescending(k => k.BusinessDay);

            var rows = new List<object>();
            foreach (var (tillId, day) in keys)
            {
                var dayEvents = events.Where(e => e.TillId == tillId && e.BusinessDay == day).ToList();
                var dayTenders = tenders.Where(t => t.TillId == tillId && t.BusinessDay == day).ToList();
                var z = dayEvents.FirstOrDefault(e => e.Type == CashEventType.ZClose);
                long floatPence = dayEvents.Where(e => e.Type == CashEventType.OpenFloat).Sum(e => e.AmountPence);
                long paidIn = dayEvents.Where(e => e.Type == CashEventType.PaidIn).Sum(e => e.AmountPence);
                long paidOut = dayEvents.Where(e => e.Type == CashEventType.PaidOut).Sum(e => e.AmountPence);
                long cash = dayTenders.Where(t => t.TenderType == TenderType.Cash).Sum(t => t.Net);
                long card = dayTenders.Where(t => t.TenderType == TenderType.Card).Sum(t => t.Net);

                rows.Add(new
                {
                    tillId,
                    businessDay = day.ToString("yyyy-MM-dd"),
                    floatPence,
                    paidInPence = paidIn,
                    paidOutPence = paidOut,
                    cashTakingsPence = cash,
                    cardTakingsPence = card,
                    expectedCashPence = floatPence + cash + paidIn - paidOut,
                    countedPence = z?.CountedPence,
                    variancePence = z?.VariancePence,
                    zClosed = z != null,
                    zClosedAtUtc = z?.ReceivedAtUtc,
                });
            }
            return Ok(rows);
        }
    }
}
