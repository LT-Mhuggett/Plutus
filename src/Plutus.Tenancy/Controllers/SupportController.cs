#nullable disable

using System;
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
using Plutus.SharedKernel;

namespace Plutus.Tenancy.Controllers
{
    public sealed record NewTicketBody(string Subject, string Body, byte Severity);
    public sealed record NewMessageBody(string Body);
    public sealed record TicketAdminBody(byte? Status, string AssignedTo);

    /// <summary>
    /// OP4 support tickets. CLIENT side (<c>/api/v1/support/*</c>, any authenticated tenant user):
    /// raise + read + reply to your OWN tenant's tickets (ambient tenant scoping isolates them).
    /// OPERATOR side (<c>/api/v1/platform/tickets*</c>, platform-admin): read across tenants, reply,
    /// set status/assignee. A new ticket or a client reply raises a keyed operator alert
    /// (<c>ticket:{id}</c>); an operator reply or close clears it. Ticket volume feeds the WP16.1
    /// <c>support-heavy</c> churn signal.
    /// </summary>
    [ApiController]
    public sealed class SupportController : ControllerBase
    {
        public const int MaxOpenPerTenant = 20;
        private static string AlertKey(Guid ticketId) => $"ticket:{ticketId}";

        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;
        private readonly IOperatorAlerter _alerter;
        public SupportController(MySqlDbContext db, ITenantContext tenant, IOperatorAlerter alerter)
        { _db = db; _tenant = tenant; _alerter = alerter; }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;
        private string ActorName => User?.Identity?.Name ?? "user";

        // ── CLIENT ─────────────────────────────────────────────────────────────

        [HttpPost("api/v1/support/tickets")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.SupportTickets)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Raise([FromBody] NewTicketBody body, CancellationToken ct)
        {
            var subject = body?.Subject?.Trim();
            var text = body?.Body?.Trim();
            if (string.IsNullOrEmpty(subject) || subject.Length > 200) return BadRequest(new { detail = "Subject is required (max 200)." });
            if (string.IsNullOrEmpty(text) || text.Length > 4000) return BadRequest(new { detail = "Message is required (max 4000)." });

            var openCount = await _db.SupportTickets.CountAsync(t => t.Status != (byte)SupportStatus.Closed, ct);
            if (openCount >= MaxOpenPerTenant) return Conflict(new { detail = "You already have the maximum number of open tickets; please wait for a reply." });

            var now = DateTime.UtcNow;
            var ticket = new SupportTicket
            {
                Id = Uuid7.New(), TenantId = _tenant.TenantId, Subject = subject,
                Status = (byte)SupportStatus.Open, Severity = body.Severity > 2 ? (byte)0 : body.Severity,
                RaisedByUserId = Actor, RaisedByName = Trunc(ActorName, 100), CreatedAtUtc = now, UpdatedAtUtc = now,
            };
            _db.SupportTickets.Add(ticket);
            _db.SupportMessages.Add(new SupportMessage
            {
                Id = Uuid7.New(), TenantId = _tenant.TenantId, TicketId = ticket.Id,
                FromOperator = false, AuthorName = ticket.RaisedByName, Body = text, AtUtc = now,
            });
            _db.CurrentUser = Actor.ToString();
            await _db.SaveChangesAsync(ct);
            await _alerter.RaiseAsync(AlertKey(ticket.Id), "support", _tenant.TenantId, "ticket", $"New ticket: {subject}", ct);
            return Created($"/api/v1/support/tickets/{ticket.Id}", new { ticket.Id });
        }

        [HttpGet("api/v1/support/tickets")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.SupportTickets)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> MyTickets(CancellationToken ct) =>
            Ok(await _db.SupportTickets.AsNoTracking().OrderByDescending(t => t.UpdatedAtUtc)
                .Select(t => new { t.Id, t.Subject, t.Status, t.Severity, t.RaisedByName, t.CreatedAtUtc, t.UpdatedAtUtc }).ToListAsync(ct));

        [HttpGet("api/v1/support/tickets/{id}/messages")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.SupportTickets)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> MyThread([FromRoute] Guid id, CancellationToken ct)
        {
            if (!await _db.SupportTickets.AnyAsync(t => t.Id == id, ct)) return NotFound(); // filter scopes to caller's tenant
            return Ok(await Thread(id, ct));
        }

        [HttpPost("api/v1/support/tickets/{id}/messages")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.SupportTickets)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ClientReply([FromRoute] Guid id, [FromBody] NewMessageBody body, CancellationToken ct)
        {
            var text = body?.Body?.Trim();
            if (string.IsNullOrEmpty(text) || text.Length > 4000) return BadRequest(new { detail = "Message is required (max 4000)." });
            var ticket = await _db.SupportTickets.FirstOrDefaultAsync(t => t.Id == id, ct); // tenant-scoped
            if (ticket == null || ticket.Status == (byte)SupportStatus.Closed) return NotFound();
            var now = DateTime.UtcNow;
            _db.SupportMessages.Add(new SupportMessage
            {
                Id = Uuid7.New(), TenantId = ticket.TenantId, TicketId = id,
                FromOperator = false, AuthorName = Trunc(ActorName, 100), Body = text, AtUtc = now,
            });
            ticket.Status = (byte)SupportStatus.Open; // client responded → back to the operator
            ticket.UpdatedAtUtc = now;
            _db.CurrentUser = Actor.ToString();
            await _db.SaveChangesAsync(ct);
            await _alerter.RaiseAsync(AlertKey(id), "support", ticket.TenantId, "ticket", $"Client replied: {ticket.Subject}", ct);
            return NoContent();
        }

        // ── OPERATOR (platform-admin, cross-tenant) ─────────────────────────────

        [HttpGet("api/v1/platform/tickets")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Inbox([FromQuery] byte? status, CancellationToken ct)
        {
            var q = _db.SupportTickets.IgnoreQueryFilters().AsNoTracking();
            if (status is byte s) q = q.Where(t => t.Status == s);
            var tickets = await q.OrderByDescending(t => t.UpdatedAtUtc).ToListAsync(ct);
            var names = (await _db.Tenants.AsNoTracking().Select(t => new { t.Id, t.Name }).ToListAsync(ct))
                .ToDictionary(x => x.Id, x => x.Name);
            return Ok(tickets.Select(t => new
            {
                t.Id, tenantId = t.TenantId, tenant = names.TryGetValue(t.TenantId, out var n) ? n : "?",
                t.Subject, t.Status, t.Severity, t.RaisedByName, t.AssignedTo, t.CreatedAtUtc, t.UpdatedAtUtc,
            }));
        }

        [HttpGet("api/v1/platform/tickets/{id}/messages")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> OperatorThread([FromRoute] Guid id, CancellationToken ct)
        {
            var msgs = await _db.SupportMessages.IgnoreQueryFilters().AsNoTracking()
                .Where(m => m.TicketId == id).OrderBy(m => m.AtUtc)
                .Select(m => new { m.FromOperator, m.AuthorName, m.Body, m.AtUtc }).ToListAsync(ct);
            return Ok(msgs);
        }

        [HttpPost("api/v1/platform/tickets/{id}/reply")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> OperatorReply([FromRoute] Guid id, [FromBody] NewMessageBody body, CancellationToken ct)
        {
            var text = body?.Body?.Trim();
            if (string.IsNullOrEmpty(text) || text.Length > 4000) return BadRequest(new { detail = "Message is required (max 4000)." });
            var ticket = await _db.SupportTickets.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id, ct);
            if (ticket == null) return NotFound();
            var now = DateTime.UtcNow;
            // Operator context is Guid.Empty → StampAndGuardTenant skips, so a tenant-owned row with
            // an explicit TenantId is allowed. Set it from the ticket.
            _db.SupportMessages.Add(new SupportMessage
            {
                Id = Uuid7.New(), TenantId = ticket.TenantId, TicketId = id,
                FromOperator = true, AuthorName = "Plutus support", Body = text, AtUtc = now,
            });
            ticket.Status = (byte)SupportStatus.WaitingOnClient;
            ticket.UpdatedAtUtc = now;
            _db.CurrentUser = Actor.ToString();
            await _db.SaveChangesAsync(ct);
            await _alerter.ClearAsync(AlertKey(id), ct); // answered — no longer needs attention
            return NoContent();
        }

        [HttpPut("api/v1/platform/tickets/{id}")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SetTicket([FromRoute] Guid id, [FromBody] TicketAdminBody body, CancellationToken ct)
        {
            var ticket = await _db.SupportTickets.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id, ct);
            if (ticket == null) return NotFound();
            _db.CurrentUser = Actor.ToString();
            if (body?.Status is byte s && s <= 2) ticket.Status = s;
            if (body?.AssignedTo != null) ticket.AssignedTo = Trunc(body.AssignedTo.Trim(), 100);
            ticket.UpdatedAtUtc = DateTime.UtcNow;
            _db.Audit(ticket.TenantId, Actor, "support.ticket", nameof(SupportTicket), id.ToString(), new { body?.Status, body?.AssignedTo });
            await _db.SaveChangesAsync(ct);
            if (ticket.Status == (byte)SupportStatus.Closed) await _alerter.ClearAsync(AlertKey(id), ct);
            return NoContent();
        }

        // ── shared ──
        private async Task<object> Thread(Guid ticketId, CancellationToken ct) =>
            await _db.SupportMessages.AsNoTracking().Where(m => m.TicketId == ticketId).OrderBy(m => m.AtUtc)
                .Select(m => new { m.FromOperator, m.AuthorName, m.Body, m.AtUtc }).ToListAsync(ct);

        private static string Trunc(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max);
    }
}
