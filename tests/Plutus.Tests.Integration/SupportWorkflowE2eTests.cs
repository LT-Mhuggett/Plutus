using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.Identity;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// **WP-TICKETS — the four gaps in the support desk.**
///
/// ⚠⚠ MATT, 2026-08-21, in one paragraph: *"When I reply to a live ticket, how is the user informed?
/// Does the heartbeat need to check for an update?"*, *"The help screen also needs to check if there
/// has been an update as it never updates without a navigation"*, and *"There is no 'Request ticket
/// to be closed' from either person, also the button 'Close' I assume closes the ticket, but there
/// is nothing visual within the ticket itself?"*
///
/// These pin the WIRE. `SupportRulesTests` owns the unread rule itself.
/// </summary>
public class SupportWorkflowE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public SupportWorkflowE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = KnownTenants.Kapow;

    private HttpRequestMessage R(HttpMethod m, string url, string token, object body = null)
    {
        var req = new HttpRequestMessage(m, url) { Content = body == null ? null : JsonContent.Create(body) };
        req.Headers.Authorization = new("Bearer", token);
        return req;
    }

    private async Task<Guid> SeedStaffAsync()
    {
        var userId = Guid.NewGuid();
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "support-wf-e2e-seed";
        await RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow);
        var cashier = await db.RbacRoles.FirstAsync(r => r.Name == "Cashier");
        db.RbacRoleAssignments.Add(new RbacRoleAssignment
        {
            Id = Uuid7.New(), TenantId = Kapow, UserId = userId, RoleId = cashier.Id,
            ScopeType = RbacScopeType.Tenant, ScopeId = "", CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return userId;
    }

    /// <summary>A raised ticket with one operator reply on it — the state the badge exists for.</summary>
    private async Task<(Guid Id, string Staff, string Admin)> SeedAnsweredTicketAsync(string subject)
    {
        var client = _f.CreateClient();
        var staff = PlutusAppFactory.OperatorTokenFor(await SeedStaffAsync(), "pos.sell");
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);

        using var raise = await client.SendAsync(R(HttpMethod.Post, "/api/v1/support/tickets", staff,
            new { subject, body = "It is not working.", severity = 1 }));
        Assert.Equal(HttpStatusCode.Created, raise.StatusCode);

        var id = await NewestTicketIdAsync(subject);

        using var reply = await client.SendAsync(R(HttpMethod.Post, $"/api/v1/platform/tickets/{id}/reply", admin,
            new { body = "Have you tried turning it off and on again?" }));
        Assert.Equal(HttpStatusCode.NoContent, reply.StatusCode);

        return (id, staff, admin);
    }

    private async Task<Guid> NewestTicketIdAsync(string subject)
    {
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        return await db.SupportTickets.IgnoreQueryFilters()
            .Where(t => t.Subject == subject)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => t.Id)
            .FirstAsync();
    }

    private async Task<int> UnreadAsync(string staff)
    {
        using var resp = await _f.CreateClient().SendAsync(R(HttpMethod.Get, "/api/v1/support/unread", staff));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        return body.GetProperty("unread").GetInt32();
    }

    // ── gap 2: the customer is told ──────────────────────────────────────────────────────────

    /// <summary>⚠⚠ THE GAP ITSELF: support replies, and until now nothing anywhere told the shop.</summary>
    [Fact]
    public async Task An_operator_reply_makes_the_ticket_unread()
    {
        var (_, staff, _) = await SeedAnsweredTicketAsync("Unread badge lights");

        Assert.True(await UnreadAsync(staff) >= 1);
    }

    /// <summary>⚠ OPENING THE THREAD CLEARS IT — the badge is a consequence of the state, so reading
    /// on one till clears it on the one beside it too.</summary>
    [Fact]
    public async Task Marking_it_read_clears_the_count()
    {
        var (id, staff, _) = await SeedAnsweredTicketAsync("Unread badge clears");

        var before = await UnreadAsync(staff);
        Assert.True(before >= 1);

        using var read = await _f.CreateClient().SendAsync(
            R(HttpMethod.Post, $"/api/v1/support/tickets/{id}/read", staff, new { }));
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);

        Assert.Equal(before - 1, await UnreadAsync(staff));
    }

    /// <summary>
    /// ⚠⚠ READING MUST NOT TOUCH `UpdatedAtUtc`. It orders the operator's inbox by "who needs
    /// attention", and a shop merely READING a thread must not push their ticket to the top of that
    /// list — it would look like activity and bury the tickets that have some.
    /// </summary>
    [Fact]
    public async Task Reading_a_thread_does_not_bump_it_up_the_operator_inbox()
    {
        var (id, staff, _) = await SeedAnsweredTicketAsync("Read does not bump");

        DateTime before;
        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            before = await db.SupportTickets.IgnoreQueryFilters().Where(t => t.Id == id)
                .Select(t => t.UpdatedAtUtc).FirstAsync();
        }

        using var read = await _f.CreateClient().SendAsync(
            R(HttpMethod.Post, $"/api/v1/support/tickets/{id}/read", staff, new { }));
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);

        using var scope2 = _f.Services.CreateScope();
        var db2 = (MySqlDbContext)scope2.ServiceProvider.GetRequiredService<RepositoryContext>();
        var after = await db2.SupportTickets.IgnoreQueryFilters().Where(t => t.Id == id)
            .Select(t => t.UpdatedAtUtc).FirstAsync();

        Assert.Equal(before, after);
    }

    // ── gap 4: closure, from either side ─────────────────────────────────────────────────────

    /// <summary>⚠⚠ A SHOP MAY ASK, NOT CLOSE. A shop closing its own open incident is how a fault
    /// gets lost, so asking does not close anything by itself.</summary>
    [Fact]
    public async Task A_client_request_does_not_close_the_ticket()
    {
        var (id, staff, _) = await SeedAnsweredTicketAsync("Client asks to close");

        using var ask = await _f.CreateClient().SendAsync(
            R(HttpMethod.Post, $"/api/v1/support/tickets/{id}/request-close", staff, new { }));
        Assert.Equal(HttpStatusCode.NoContent, ask.StatusCode);

        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var t = await db.SupportTickets.IgnoreQueryFilters().FirstAsync(x => x.Id == id);

        Assert.NotEqual((byte)SupportStatus.Closed, t.Status);
        Assert.False(t.ClosureRequestedByOperator);      // false = the CLIENT asked
        Assert.NotNull(t.ClosureRequestedAtUtc);
    }

    /// <summary>⚠ AND WITHOUT SUPPORT HAVING ASKED, the shop cannot accept its way to a close.</summary>
    [Fact]
    public async Task A_client_cannot_accept_a_close_nobody_offered()
    {
        var (id, staff, _) = await SeedAnsweredTicketAsync("No offer to accept");

        using var accept = await _f.CreateClient().SendAsync(
            R(HttpMethod.Post, $"/api/v1/support/tickets/{id}/accept-close", staff, new { }));

        Assert.Equal(HttpStatusCode.BadRequest, accept.StatusCode);
    }

    /// <summary>⚠⚠ THE POLITE PATH END TO END: support asks, the shop agrees, and it closes — with a
    /// date, which is what the thread renders.</summary>
    [Fact]
    public async Task Support_asks_the_client_agrees_and_it_closes_with_a_date()
    {
        var (id, staff, admin) = await SeedAnsweredTicketAsync("Operator asks to close");
        var client = _f.CreateClient();

        using var ask = await client.SendAsync(R(HttpMethod.Post, $"/api/v1/platform/tickets/{id}/request-close", admin, new { }));
        Assert.Equal(HttpStatusCode.NoContent, ask.StatusCode);

        using var accept = await client.SendAsync(R(HttpMethod.Post, $"/api/v1/support/tickets/{id}/accept-close", staff, new { }));
        Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);

        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var t = await db.SupportTickets.IgnoreQueryFilters().FirstAsync(x => x.Id == id);

        Assert.Equal((byte)SupportStatus.Closed, t.Status);
        Assert.NotNull(t.ClosedAtUtc);
        // ⚠ A closed ticket showing "support has asked to close this" argues with itself.
        Assert.Null(t.ClosureRequestedByOperator);
    }

    /// <summary>⚠ Either party may end the question — keep-open withdraws or declines.</summary>
    [Fact]
    public async Task Keeping_it_open_withdraws_the_request()
    {
        var (id, staff, admin) = await SeedAnsweredTicketAsync("Keep it open");
        var client = _f.CreateClient();

        using var ask = await client.SendAsync(R(HttpMethod.Post, $"/api/v1/platform/tickets/{id}/request-close", admin, new { }));
        Assert.Equal(HttpStatusCode.NoContent, ask.StatusCode);

        using var keep = await client.SendAsync(R(HttpMethod.Post, $"/api/v1/support/tickets/{id}/keep-open", staff, new { }));
        Assert.Equal(HttpStatusCode.NoContent, keep.StatusCode);

        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var t = await db.SupportTickets.IgnoreQueryFilters().FirstAsync(x => x.Id == id);

        Assert.Null(t.ClosureRequestedByOperator);
        Assert.Null(t.ClosureRequestedAtUtc);
        Assert.NotEqual((byte)SupportStatus.Closed, t.Status);
    }

    /// <summary>⚠ THE OPERATOR UNILATERAL CLOSE STILL STAMPS A DATE — the path that already existed,
    /// and the one Matt was looking at when he said the thread showed nothing.</summary>
    [Fact]
    public async Task The_operators_close_button_stamps_a_date()
    {
        var (id, _, admin) = await SeedAnsweredTicketAsync("Operator closes outright");

        using var close = await _f.CreateClient().SendAsync(
            R(HttpMethod.Put, $"/api/v1/platform/tickets/{id}", admin, new { status = 2 }));
        Assert.Equal(HttpStatusCode.NoContent, close.StatusCode);

        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var t = await db.SupportTickets.IgnoreQueryFilters().FirstAsync(x => x.Id == id);

        Assert.Equal((byte)SupportStatus.Closed, t.Status);
        Assert.NotNull(t.ClosedAtUtc);
    }

    /// <summary>⚠ AND A CLOSED TICKET STOPS BADGING — a notification for a finished conversation is
    /// one people learn to ignore, and then they ignore the one that matters.</summary>
    [Fact]
    public async Task Closing_a_ticket_stops_it_being_unread()
    {
        var (id, staff, admin) = await SeedAnsweredTicketAsync("Closed stops badging");

        var before = await UnreadAsync(staff);
        Assert.True(before >= 1);

        using var close = await _f.CreateClient().SendAsync(
            R(HttpMethod.Put, $"/api/v1/platform/tickets/{id}", admin, new { status = 2 }));
        Assert.Equal(HttpStatusCode.NoContent, close.StatusCode);

        Assert.Equal(before - 1, await UnreadAsync(staff));
    }

    // ── gap 1: the summary ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ MATT: *"There needs to be a summary view of all tickets. Today, 7 days, last month, last
    /// 90 days. Which clients have raised etc."*
    ///
    /// ⚠ COUNTED BY STATUS AS WELL AS AGE — "12 tickets this week" with 11 closed is a good week and
    /// reads as a bad one.
    /// </summary>
    [Fact]
    public async Task The_summary_answers_four_windows_and_a_client_breakdown()
    {
        await SeedAnsweredTicketAsync("Summary window");
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);

        using var resp = await _f.CreateClient().SendAsync(
            R(HttpMethod.Get, "/api/v1/platform/tickets/summary", admin));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        var windows = body.GetProperty("windows").EnumerateArray().ToList();

        Assert.Equal(4, windows.Count);
        Assert.Equal(new[] { "Today", "7 days", "30 days", "90 days" },
            windows.Select(w => w.GetProperty("label").GetString()).ToArray());

        // Every window carries the status split, not just a total.
        Assert.All(windows, w =>
        {
            Assert.True(w.TryGetProperty("open", out _));
            Assert.True(w.TryGetProperty("closed", out _));
            Assert.True(w.TryGetProperty("urgent", out _));
        });

        // Today's window must contain the ticket just raised.
        Assert.True(windows[0].GetProperty("raised").GetInt32() >= 1);

        // ⚠ And the client breakdown names who asked.
        var byClient = body.GetProperty("byClient").EnumerateArray().ToList();
        Assert.NotEmpty(byClient);
        Assert.Contains(byClient, c => c.GetProperty("raised").GetInt32() >= 1);
    }

    /// <summary>⚠ The summary is platform-admin only — a shop must not see other shops' ticket
    /// counts, and the by-client table names every tenant.</summary>
    [Fact]
    public async Task A_shop_cannot_read_the_operator_summary()
    {
        var staff = PlutusAppFactory.OperatorTokenFor(await SeedStaffAsync(), "pos.sell");

        using var resp = await _f.CreateClient().SendAsync(
            R(HttpMethod.Get, "/api/v1/platform/tickets/summary", staff));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
