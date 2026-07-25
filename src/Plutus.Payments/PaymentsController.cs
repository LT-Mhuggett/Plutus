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

namespace Plutus.Payments
{
    public sealed class PaymentEventBody
    {
        public Guid EventId { get; set; }          // client/webhook-minted (idempotency anchor)
        public string ProviderRef { get; set; }
        public long AmountPence { get; set; }
        public DateTime CapturedAtUtc { get; set; }
        public string Provider { get; set; }       // defaults to the configured provider name
    }

    /// <summary>
    /// WP7.1 endpoints. Capture events come from the till (post-capture confirmation) or a
    /// provider webhook — same principals as sale ingest. The unresolved queue and reconcile
    /// trigger are portal surfaces (financials).
    /// </summary>
    [ApiController]
    public sealed class PaymentsController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;
        private readonly PaymentReconciliationService _reconciliation;
        private readonly IPaymentProvider _provider;

        public PaymentsController(
            MySqlDbContext db, ITenantContext tenant,
            PaymentReconciliationService reconciliation, IPaymentProvider provider)
        {
            _db = db;
            _tenant = tenant;
            _reconciliation = reconciliation;
            _provider = provider;
        }

        /// <summary>Idempotent capture notification (D13: the terminal ref is written into
        /// the pending sale BEFORE capture — this event is the capture side of that pair).</summary>
        [HttpPost("api/v1/payments/events")]
        [Authorize(Policy = PlutusPolicies.SalesIngest)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Capture([FromBody] PaymentEventBody body)
        {
            if (body == null || body.EventId == Guid.Empty || string.IsNullOrWhiteSpace(body.ProviderRef))
                return BadRequest(new { detail = "eventId and providerRef are required." });
            if (body.AmountPence <= 0)
                return BadRequest(new { detail = "amountPence must be positive." });

            var existing = await _db.PaymentEvents.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == body.EventId);
            if (existing != null) return Ok(new { eventId = existing.Id, resolved = existing.ResolvedAtUtc != null });

            _db.CurrentUser = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "device";
            var evt = new PaymentEvent
            {
                Id = body.EventId, TenantId = _tenant.TenantId,
                Provider = string.IsNullOrWhiteSpace(body.Provider) ? _provider.Name : body.Provider.Trim(),
                ProviderRef = body.ProviderRef.Trim(), AmountPence = body.AmountPence,
                Status = PaymentEventStatus.Captured,
                CapturedAtUtc = body.CapturedAtUtc == default ? DateTime.UtcNow : body.CapturedAtUtc,
                ReceivedAtUtc = DateTime.UtcNow,
            };
            try
            {
                _db.PaymentEvents.Add(evt);
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                _db.ChangeTracker.Clear();
                var raced = await _db.PaymentEvents.IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == body.EventId);
                if (raced != null) return Ok(new { eventId = raced.Id, resolved = raced.ResolvedAtUtc != null });
                throw;
            }

            // opportunistic match — the sale usually arrived milliseconds earlier
            await _reconciliation.ReconcileAsync();
            var fresh = await _db.PaymentEvents.AsNoTracking().FirstAsync(p => p.Id == evt.Id);
            return Created($"/api/v1/payments/events/{evt.Id}", new { eventId = evt.Id, resolved = fresh.ResolvedAtUtc != null });
        }

        /// <summary>The orphaned-payment queue: captures with no matching recorded tender.</summary>
        [HttpGet("api/v1/payments/unresolved")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalFinancialsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Unresolved() =>
            Ok(await _db.PaymentEvents.AsNoTracking()
                .Where(p => p.ResolvedAtUtc == null)
                .OrderBy(p => p.CapturedAtUtc)
                .Select(p => new
                {
                    eventId = p.Id, provider = p.Provider, providerRef = p.ProviderRef,
                    amountPence = p.AmountPence, capturedAtUtc = p.CapturedAtUtc,
                    ageMinutes = (int)(DateTime.UtcNow - p.CapturedAtUtc).TotalMinutes,
                })
                .ToListAsync());

        /// <summary>Re-run matching (e.g. after an offline till drained its outbox).</summary>
        [HttpPost("api/v1/payments/reconcile")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalFinancialsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Reconcile()
        {
            var (matched, unresolved) = await _reconciliation.ReconcileAsync();
            return Ok(new { matched, unresolved, provider = _provider.Name });
        }
    }
}
