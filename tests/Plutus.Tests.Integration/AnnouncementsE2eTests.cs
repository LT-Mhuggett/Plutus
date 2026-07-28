using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// WP15.1 announcements: authoring is platform-admin; /active is tenant-scoped (a targeted
/// announcement reaches only that tenant), time-bounded (expired ones drop off), and a global
/// one reaches everyone.
/// </summary>
public class AnnouncementsE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public AnnouncementsE2eTests(PlutusAppFactory f) => _f = f;

    private async Task<string> ActiveBody(Guid tenant)
    {
        var client = _f.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/announcements/active");
        req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken("pos.sell", tenant));
        return await (await client.SendAsync(req)).Content.ReadAsStringAsync();
    }

    private async Task<HttpStatusCode> Author(string token, object body)
    {
        var client = _f.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/announcements") { Content = JsonContent.Create(body) };
        req.Headers.Authorization = new("Bearer", token);
        return (await client.SendAsync(req)).StatusCode;
    }

    [Fact]
    public async Task Active_is_tenant_scoped_and_time_bounded()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);

        // non-admin can't author
        Assert.Equal(HttpStatusCode.Forbidden,
            await Author(PlutusAppFactory.OperatorToken("pos.sell"), new { severity = 2, title = "x", body = "", startsAtUtc = now, endsAtUtc = now.AddHours(1) }));

        // targeted to tenant A, live now
        Assert.Equal(HttpStatusCode.Created, await Author(admin, new { severity = 2, title = "A-only incident", body = "for A", startsAtUtc = now.AddHours(-1), endsAtUtc = now.AddHours(1), tenantIds = new[] { a } }));
        // global, live now
        Assert.Equal(HttpStatusCode.Created, await Author(admin, new { severity = 0, title = "Everyone notice", body = "all", startsAtUtc = now.AddHours(-1), endsAtUtc = now.AddHours(1), tenantIds = (Guid[])null }));
        // expired
        Assert.Equal(HttpStatusCode.Created, await Author(admin, new { severity = 0, title = "Old expired", body = "", startsAtUtc = now.AddHours(-2), endsAtUtc = now.AddHours(-1) }));

        var seenByA = await ActiveBody(a);
        Assert.Contains("A-only incident", seenByA);
        Assert.Contains("Everyone notice", seenByA);
        Assert.DoesNotContain("Old expired", seenByA);       // time-bounded

        var seenByB = await ActiveBody(b);
        Assert.Contains("Everyone notice", seenByB);
        Assert.DoesNotContain("A-only incident", seenByB);   // tenant-scoped
    }
}
