using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// The Phase 6 pick-from-floor notifications, till-facing. These endpoints shipped gated on
/// "perm:sales.ingest" — the SCOPE-policy name used as a PERMISSION code, which no RBAC role can
/// hold — so every till poll 403'd silently into its .catch from WP6.2 until 2026-08-07, and the
/// broken gate hid a missing db.CurrentUser in the ack path behind it. Found by the MAUI-retrofit
/// readiness audit; this test is the one that would have caught both on day one.
/// </summary>
public class PickNotesE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public PickNotesE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    private HttpRequestMessage Req(HttpMethod m, string url, string token)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Authorization = new("Bearer", token);
        return req;
    }

    [Fact]
    public async Task Till_can_list_and_ack_a_pick_note_and_ack_is_idempotent()
    {
        var noteId = Uuid7.New();
        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            db.CurrentUser = "picknotes-e2e-seed";
            db.WebstoreNotifications.Add(new WebstoreNotification
            {
                Id = noteId, TenantId = Kapow, WebStoreId = Uuid7.New(), StoreId = null,
                WooOrderId = 4242, Message = "Web order #4242 sold 1 × shop-floor item — pull it off the shelf",
                CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var client = _f.CreateClient();

        // an ORDINARY pos.sell operator token — the exact identity that 403'd for a month
        var till = PlutusAppFactory.OperatorToken("pos.sell", Kapow);
        var list = await client.SendAsync(Req(HttpMethod.Get, "/api/v1/notifications?unackedOnly=true", till));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains("4242", await list.Content.ReadAsStringAsync());

        // ack succeeds (this save is where the hidden CurrentUser fault lived) and is idempotent
        Assert.Equal(HttpStatusCode.OK,
            (await client.SendAsync(Req(HttpMethod.Post, $"/api/v1/notifications/{noteId}/ack", till))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.SendAsync(Req(HttpMethod.Post, $"/api/v1/notifications/{noteId}/ack", till))).StatusCode);

        // acked → gone from the unacked poll
        var after = await client.SendAsync(Req(HttpMethod.Get, "/api/v1/notifications?unackedOnly=true", till));
        Assert.DoesNotContain(noteId.ToString(), await after.Content.ReadAsStringAsync());

        // still 401 for the unauthenticated
        using var anon = new HttpRequestMessage(HttpMethod.Get, "/api/v1/notifications");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(anon)).StatusCode);
    }

    /// <summary>
    /// ⚠ A till must not see, or acknowledge, a pick note belonging to another tenant.
    ///
    /// Neither endpoint carries a tenant predicate of its own — unlike its neighbours in the same
    /// controller, which are written <c>.Where(r =&gt; r.TenantId == _tenant.TenantId)</c>. That
    /// reads like an omission and is not: <c>WebstoreNotification</c> is in
    /// <c>MySqlDbContext.TenantOwned</c>, so <c>SetTenantFilter</c> installs a global query filter
    /// and EF scopes both queries automatically. **This test exists because "it's fine, the filter
    /// covers it" is exactly the kind of thing that is true until someone adds
    /// <c>IgnoreQueryFilters()</c> while chasing an unrelated bug** — and `CreateForRecordedAsync`
    /// already calls that, ten lines away, for its legitimate dedupe check.
    ///
    /// Nothing pinned it before: the original test seeded one tenant and asked one tenant.
    ///
    /// The ack matters more than the read. It is a cross-tenant WRITE, and acking somebody else's
    /// note clears it from THEIR shop floor's unacked poll — their staff never pull the stock, and
    /// the web order ships short. Note the layering: even if the read filter were removed, the
    /// write would still be caught by <c>StampAndGuardTenant</c>, so this pins defence in depth
    /// rather than a single check.
    /// </summary>
    [Fact]
    public async Task A_till_cannot_see_or_ack_another_tenants_pick_note()
    {
        // Seeded in Kapow and asked for as a FOREIGN tenant — the direction that works with the
        // write guard, which refuses to create another tenant's row in the first place.
        var noteId = Uuid7.New();
        const int orderNo = 987654;
        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            db.CurrentUser = "picknotes-isolation-seed";
            db.WebstoreNotifications.Add(new WebstoreNotification
            {
                Id = noteId, TenantId = Kapow, WebStoreId = Uuid7.New(), StoreId = null,
                WooOrderId = orderNo,
                Message = $"Web order #{orderNo}: Kapow's stock — must never reach another tenant's till",
                CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var client = _f.CreateClient();
        var foreignTill = PlutusAppFactory.OperatorToken("pos.sell", Guid.NewGuid());

        // 1. the read — Kapow's note must not appear in another tenant's poll, by id or by the
        //    order number in the message body, which is the part that is actually trading data.
        var list = await client.SendAsync(Req(HttpMethod.Get, "/api/v1/notifications?unackedOnly=true", foreignTill));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var body = await list.Content.ReadAsStringAsync();
        Assert.DoesNotContain(noteId.ToString(), body);
        Assert.DoesNotContain(orderNo.ToString(), body);

        // 2. the write — acking it must fail. NotFound, not Forbid: a 403 would confirm the id
        //    exists, which is itself a cross-tenant disclosure.
        var ack = await client.SendAsync(Req(HttpMethod.Post, $"/api/v1/notifications/{noteId}/ack", foreignTill));
        Assert.Equal(HttpStatusCode.NotFound, ack.StatusCode);

        // 3. and it is still unacked for Kapow — proving the ack was REFUSED, not merely hidden
        //    from the foreign tenant's response.
        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var note = await db.WebstoreNotifications.FirstOrDefaultAsync(n => n.Id == noteId);
            Assert.NotNull(note);
            Assert.Null(note!.AckedAtUtc);
        }
    }
}
