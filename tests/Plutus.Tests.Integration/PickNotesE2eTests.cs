using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
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
}
