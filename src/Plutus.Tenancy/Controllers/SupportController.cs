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
                .Select(t => new { t.Id, t.Subject, t.Status, t.Severity, t.RaisedByName, t.CreatedAtUtc, t.UpdatedAtUtc,
                    // ⚠ WP-TICKETS: the thread has to be able to show that it CLOSED, and who asked.
                    t.ClosedAtUtc, t.ClosureRequestedByOperator, t.ClosureRequestedAtUtc, t.ClientLastReadAtUtc }).ToListAsync(ct));

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
                t.Subject, t.Status, t.Severity, t.RaisedByName, t.AssignedTo, t.CreatedAtUtc, t.UpdatedAtUtc, t.ClosedAtUtc, t.ClosureRequestedByOperator, t.ClosureRequestedAtUtc,
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
            // ⚠⚠ THROUGH `Close`, SO `ClosedAtUtc` IS ALWAYS STAMPED (WP-TICKETS, 2026-08-21). Matt:
            // *"the button 'Close' I assume closes the ticket, but there is nothing visual within the
            // ticket itself?"* It did close it and the thread said nothing — because there was no
            // close DATE to render, only a status byte in a list the reader was not looking at.
            //
            // ⚠ A close that forgot the stamp would render a closed ticket that still looks open,
            // which is the exact fault being fixed. One helper, so no path can forget.
            if (body?.Status is byte s && s <= 2)
            {
                if (s == (byte)SupportStatus.Closed) Close(ticket);
                else
                {
                    ticket.Status = s;
                    // ⚠ REOPENING CLEARS THE CLOSE DATE. A reopened ticket showing "closed on the 4th"
                    // is a thread contradicting itself.
                    ticket.ClosedAtUtc = null;
                }
            }
            if (body?.AssignedTo != null) ticket.AssignedTo = Trunc(body.AssignedTo.Trim(), 100);
            ticket.UpdatedAtUtc = DateTime.UtcNow;
            _db.Audit(ticket.TenantId, Actor, "support.ticket", nameof(SupportTicket), id.ToString(), new { body?.Status, body?.AssignedTo });
            await _db.SaveChangesAsync(ct);
            if (ticket.Status == (byte)SupportStatus.Closed) await _alerter.ClearAsync(AlertKey(id), ct);
            return NoContent();
        }


        // ── WP-TICKETS, 2026-08-21 — the four gaps ──────────────────────────────────────────────

        /// <summary>
        /// How many of this client's tickets are waiting to be READ.
        ///
        /// ⚠⚠ MATT: *"When I reply to a live ticket, how is the user informed?"* Until this, they
        /// were not. This is what the ❓ badge on both tills and the portal reads, and it is
        /// deliberately the cheapest question the support desk can answer — a count, no bodies, no
        /// thread — because the heartbeat asks it every minute on every till in the estate.
        ///
        /// ⚠ `SupportRules.IsUnreadByClient` DECIDES, not this method. The tills need the same
        /// answer offline-ish and the portal renders the same badge; three implementations of "is
        /// this unread" would be three badges that disagree.
        /// </summary>
        [HttpGet("api/v1/support/unread")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.SupportTickets)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Unread(CancellationToken ct)
        {
            var rows = await UnreadRowsAsync(ct);

            return Ok(new
            {
                unread = rows.Count,
                // ⚠ The newest first — a badge that says "3" wants a tooltip naming the most recent,
                // and the caller should not have to fetch the whole list to build one.
                subjects = rows.OrderByDescending(r => r.At).Take(5).Select(r => r.Subject).ToList(),
            });
        }

        /// <summary>
        /// Mark a thread as read by the client side.
        ///
        /// ⚠ CALLED WHEN THE THREAD IS OPENED, not when a badge is clicked. The badge is a
        /// consequence of the state, never the owner of it — clearing it from the UI would leave a
        /// second till still lit, which is the same fault in a different seat.
        ///
        /// ⚠ IDEMPOTENT and never 400s on an already-read thread. It runs on every open.
        /// </summary>
        [HttpPost("api/v1/support/tickets/{id}/read")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.SupportTickets)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> MarkRead([FromRoute] Guid id, CancellationToken ct)
        {
            var ticket = await _db.SupportTickets.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (ticket == null) return NotFound();

            ticket.ClientLastReadAtUtc = DateTime.UtcNow;

            // ⚠⚠ `UpdatedAtUtc` IS NOT TOUCHED. It orders the operator's inbox by "who needs
            // attention", and a shop merely READING a thread must not push their ticket to the top
            // of that list — it would look like activity and bury the tickets that have some.
            _db.CurrentUser = Actor.ToString();
            await _db.SaveChangesAsync(ct);
            return NoContent();
        }

        /// <summary>
        /// Ask for a ticket to be closed — **from either side**.
        ///
        /// ⚠⚠ MATT: *"There is no 'Request ticket to be closed' from either person."* Correct: the
        /// only way a ticket ended was the operator's `Close` button, so a shop whose problem was
        /// solved had no way to say so and an operator who thought it was done had no way to ask.
        ///
        /// ⚠⚠ **FROM THE CLIENT IT IS A REQUEST, NOT A CLOSE.** A shop closing its own open incident
        /// is how a fault gets lost — the operator confirms. From the operator it is also a request,
        /// because asking is the polite path; they keep the unilateral `Close` on `SetTicket` for
        /// when support has to end a thread.
        ///
        /// ⚠ POSTING AGAIN FROM THE SAME SIDE IS A NO-OP, not an error — a double-click must not
        /// produce two "asked to close" events in a thread.
        ///
        /// ⚠ AND THE OTHER SIDE ASKING **REPLACES** the request rather than stacking: two open
        /// requests pointing opposite ways is a state nothing could render honestly.
        /// </summary>
        [HttpPost("api/v1/support/tickets/{id}/request-close")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.SupportTickets)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public Task<IActionResult> ClientRequestClose([FromRoute] Guid id, CancellationToken ct) =>
            RequestCloseAsync(id, byOperator: false, ct);

        /// <summary>The operator's half. ⚠ Same flow, opposite direction — see the client's.</summary>
        [HttpPost("api/v1/platform/tickets/{id}/request-close")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public Task<IActionResult> OperatorRequestClose([FromRoute] Guid id, CancellationToken ct) =>
            RequestCloseAsync(id, byOperator: true, ct);

        /// <summary>
        /// Withdraw a closure request, or decline the other side's.
        ///
        /// ⚠ ONE ENDPOINT FOR BOTH, because they are the same state change: the request goes away.
        /// Two endpoints would need a rule about who may clear whose, and the honest rule is that
        /// either side may — a request is a question, and either party can end it.
        /// </summary>
        [HttpPost("api/v1/support/tickets/{id}/keep-open")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.SupportTickets)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> KeepOpen([FromRoute] Guid id, CancellationToken ct)
        {
            var ticket = await _db.SupportTickets.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (ticket == null) return NotFound();

            ticket.ClosureRequestedByOperator = null;
            ticket.ClosureRequestedAtUtc = null;
            ticket.UpdatedAtUtc = DateTime.UtcNow;

            _db.CurrentUser = Actor.ToString();
            await _db.SaveChangesAsync(ct);
            return NoContent();
        }

        /// <summary>
        /// The client agrees to close.
        ///
        /// ⚠⚠ THE ONLY WAY A CLIENT MAY CLOSE ANYTHING, and only when the OPERATOR asked first. It
        /// is not "the shop can close its own ticket" by another name: without a standing operator
        /// request this refuses, so a fault cannot be made to disappear from the shop's side.
        /// </summary>
        [HttpPost("api/v1/support/tickets/{id}/accept-close")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.SupportTickets)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> AcceptClose([FromRoute] Guid id, CancellationToken ct)
        {
            var ticket = await _db.SupportTickets.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (ticket == null) return NotFound();

            if (ticket.ClosureRequestedByOperator != true)
                return BadRequest(new { detail = "Plutus support hasn't asked to close this ticket." });

            Close(ticket);
            _db.CurrentUser = Actor.ToString();
            _db.Audit(ticket.TenantId, Actor, "support.ticket.closed-by-client",
                nameof(SupportTicket), id.ToString(), new { });
            await _db.SaveChangesAsync(ct);
            await _alerter.ClearAsync(AlertKey(id), ct);
            return NoContent();
        }

        /// <summary>
        /// The operator's ticket summary — **Today / 7 / 30 / 90 days, and by client**.
        ///
        /// ⚠⚠ MATT: *"There needs to be a summary view of all tickets. Today, 7 days, last month,
        /// last 90 days. Which clients have raised etc."* The inbox was a flat list ordered by last
        /// update, so "is this week worse than last" and "which customer is struggling" were
        /// questions you answered by counting rows on screen.
        ///
        /// ⚠⚠ **COUNTED BY STATUS AS WELL AS BY AGE.** "12 tickets this week" with 11 closed is a
        /// good week and reads as a bad one. Every window carries raised / still-open / closed, and
        /// the per-client rows carry the same split.
        ///
        /// ⚠ WINDOWS ARE BY `CreatedAtUtc` — "raised in the last 7 days". Bucketing on `UpdatedAtUtc`
        /// would move an old ticket into this week because somebody replied to it, which answers a
        /// different question from the one asked.
        /// </summary>
        [HttpGet("api/v1/platform/tickets/summary")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Summary(CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var today = now.Date;

            var tickets = await _db.SupportTickets.IgnoreQueryFilters().AsNoTracking()
                .Select(t => new { t.TenantId, t.Status, t.Severity, t.CreatedAtUtc, t.UpdatedAtUtc })
                .ToListAsync(ct);

            var names = (await _db.Tenants.AsNoTracking().Select(t => new { t.Id, t.Name }).ToListAsync(ct))
                .ToDictionary(x => x.Id, x => x.Name);

            object Window(string label, DateTime since)
            {
                var inWindow = tickets.Where(t => t.CreatedAtUtc >= since).ToList();
                return new
                {
                    label,
                    raised = inWindow.Count,
                    open = inWindow.Count(t => t.Status != (byte)SupportStatus.Closed),
                    closed = inWindow.Count(t => t.Status == (byte)SupportStatus.Closed),
                    // ⚠ URGENT IS BROKEN OUT because it is the number somebody scans for. A window
                    // with one urgent among forty questions is not the same week as forty questions.
                    urgent = inWindow.Count(t => t.Severity == (byte)SupportSeverity.Urgent),
                };
            }

            var windows = new[]
            {
                Window("Today", today),
                Window("7 days", today.AddDays(-6)),
                Window("30 days", today.AddDays(-29)),
                Window("90 days", today.AddDays(-89)),
            };

            // ⚠ WHICH CLIENTS HAVE RAISED — over 90 days, the widest window above, so the table and
            // the last pill agree. A per-client list over "all time" would keep rows for customers
            // who have not asked anything in two years.
            var since90 = today.AddDays(-89);
            var byClient = tickets
                .Where(t => t.CreatedAtUtc >= since90)
                .GroupBy(t => t.TenantId)
                .Select(g => new
                {
                    tenantId = g.Key,
                    tenant = names.TryGetValue(g.Key, out var n) ? n : "?",
                    raised = g.Count(),
                    open = g.Count(t => t.Status != (byte)SupportStatus.Closed),
                    urgent = g.Count(t => t.Severity == (byte)SupportSeverity.Urgent),
                    lastAtUtc = g.Max(t => t.UpdatedAtUtc),
                })
                .OrderByDescending(x => x.open).ThenByDescending(x => x.raised)
                .ToList();

            // ⚠⚠ THE OLDEST STILL-OPEN TICKET, because an average says nothing about the one that
            // has been ignored for three weeks — and that is the one that loses a customer.
            var oldestOpen = tickets
                .Where(t => t.Status != (byte)SupportStatus.Closed)
                .OrderBy(t => t.CreatedAtUtc)
                .Select(t => (DateTime?)t.CreatedAtUtc)
                .FirstOrDefault();

            return Ok(new
            {
                generatedAtUtc = now,
                windows,
                byClient,
                openTotal = tickets.Count(t => t.Status != (byte)SupportStatus.Closed),
                oldestOpenAtUtc = oldestOpen,
            });
        }

        // ── WP-TICKETS helpers ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// The client's tickets that are waiting to be read, with the message that makes them so.
        ///
        /// ⚠ ONE QUERY FOR THE THREADS AND ONE FOR THEIR LAST MESSAGE, then the decision in memory
        /// via `SupportRules.IsUnreadByClient`. A shop has a handful of tickets; the alternative is
        /// a correlated subquery per ticket on a path the heartbeat hits every minute.
        /// </summary>
        private async Task<List<(Guid Id, string Subject, DateTime At)>> UnreadRowsAsync(CancellationToken ct)
        {
            var open = await _db.SupportTickets.AsNoTracking()
                .Where(t => t.Status != (byte)SupportStatus.Closed)
                .Select(t => new { t.Id, t.Subject, t.Status, t.ClientLastReadAtUtc })
                .ToListAsync(ct);

            if (open.Count == 0) return new List<(Guid, string, DateTime)>();

            var ids = open.Select(t => t.Id).ToList();

            var last = (await _db.SupportMessages.AsNoTracking()
                    .Where(m => ids.Contains(m.TicketId))
                    .Select(m => new { m.TicketId, m.FromOperator, m.AtUtc })
                    .ToListAsync(ct))
                .GroupBy(m => m.TicketId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.AtUtc).First());

            var result = new List<(Guid, string, DateTime)>();

            foreach (var t in open)
            {
                if (!last.TryGetValue(t.Id, out var m)) continue;

                if (SupportRules.IsUnreadByClient(t.Status, m.FromOperator, m.AtUtc, t.ClientLastReadAtUtc))
                    result.Add((t.Id, t.Subject, m.AtUtc));
            }

            return result;
        }

        /// <summary>⚠ Both directions of "please close this" land here, so the rules about replacing
        /// an opposing request and about a repeat being a no-op live in ONE place.</summary>
        private async Task<IActionResult> RequestCloseAsync(Guid id, bool byOperator, CancellationToken ct)
        {
            var q = byOperator
                ? _db.SupportTickets.IgnoreQueryFilters()
                : _db.SupportTickets;

            var ticket = await q.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (ticket == null) return NotFound();

            // ⚠ A CLOSED TICKET CANNOT BE ASKED TO CLOSE. Silently succeeding would render a
            // "waiting to close" banner on a finished thread.
            if (ticket.Status == (byte)SupportStatus.Closed) return NoContent();

            // ⚠ A repeat from the same side changes nothing — a double-click must not restamp the
            // request and make it look newly asked.
            if (ticket.ClosureRequestedByOperator == byOperator) return NoContent();

            ticket.ClosureRequestedByOperator = byOperator;
            ticket.ClosureRequestedAtUtc = DateTime.UtcNow;
            ticket.UpdatedAtUtc = DateTime.UtcNow;

            _db.CurrentUser = Actor.ToString();
            await _db.SaveChangesAsync(ct);
            return NoContent();
        }

        /// <summary>
        /// Close a ticket, recording WHEN.
        ///
        /// ⚠ ONE PLACE, so no path can close a ticket without stamping `ClosedAtUtc` — the field the
        /// thread's "this was closed on …" line is drawn from. A close that forgot it would render a
        /// closed ticket that still looks open, which is the fault being fixed.
        ///
        /// ⚠ AND IT CLEARS ANY STANDING REQUEST. A closed ticket showing "support has asked to close
        /// this" is a thread arguing with itself.
        /// </summary>
        private static void Close(SupportTicket ticket)
        {
            ticket.Status = (byte)SupportStatus.Closed;
            ticket.ClosedAtUtc = DateTime.UtcNow;
            ticket.UpdatedAtUtc = DateTime.UtcNow;
            ticket.ClosureRequestedByOperator = null;
            ticket.ClosureRequestedAtUtc = null;
        }
        // ── shared ──
        private async Task<object> Thread(Guid ticketId, CancellationToken ct) =>
            await _db.SupportMessages.AsNoTracking().Where(m => m.TicketId == ticketId).OrderBy(m => m.AtUtc)
                .Select(m => new { m.FromOperator, m.AuthorName, m.Body, m.AtUtc }).ToListAsync(ct);

        private static string Trunc(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max);
    }
}
