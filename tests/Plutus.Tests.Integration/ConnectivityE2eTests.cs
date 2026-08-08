using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// The connection indicator, against the REAL controllers.
///
/// The unit tests pin the probe's logic against a scripted handler; these pin that the logic is
/// wired to endpoints that actually exist and actually behave that way — the gap where a perfectly
/// tested client meets a route that returns 404, or an "anonymous" endpoint that quietly requires a
/// token and so reports every un-enrolled till as a dead server.
/// </summary>
public class ConnectivityE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public ConnectivityE2eTests(PlutusAppFactory f) => _f = f;

    private sealed class InMemoryCredentials : IDeviceCredentialStore
    {
        public Guid? DeviceId { get; private set; }
        public string? ClientSecret { get; private set; }
        public void Save(Guid deviceId, string clientSecret) { DeviceId = deviceId; ClientSecret = clientSecret; }
        public void Clear() { DeviceId = null; ClientSecret = null; }
    }

    private async Task<string> ProvisionTillCodeAsync(HttpClient c, string email)
    {
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);
        using var pReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tenants")
        { Content = JsonContent.Create(new { name = "Conn " + email, plan = "standard", adminEmail = email, adminPassword = "S3cret!" }) };
        pReq.Headers.Authorization = new("Bearer", admin);
        var pRes = await c.SendAsync(pReq);
        Assert.Equal(HttpStatusCode.Created, pRes.StatusCode);
        var pBody = JsonDocument.Parse(await pRes.Content.ReadAsStringAsync()).RootElement;
        var tenantId = pBody.GetProperty("tenantId").GetGuid();
        var storeId = pBody.GetProperty("storeId").GetInt32();

        var portal = PlutusAppFactory.OperatorToken(PlutusPolicies.PortalTillsEnrol, tenantId);
        using var tReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tills")
        { Content = JsonContent.Create(new { storeId, name = "Counter 1" }) };
        tReq.Headers.Authorization = new("Bearer", portal);
        var tRes = await c.SendAsync(tReq);
        Assert.Equal(HttpStatusCode.Created, tRes.StatusCode);
        return JsonDocument.Parse(await tRes.Content.ReadAsStringAsync()).RootElement
            .GetProperty("enrolmentCode").GetString()!;
    }

    [Fact]
    public async Task Ping_is_anonymous_cheap_and_carries_the_server_clock()
    {
        // ⚠ If this ever needs a token, the probe can no longer tell "server down" from "till not
        // enrolled" — which is the entire feature. That is why it is asserted with a bare client.
        var http = _f.CreateClient();

        var res = await http.GetAsync("/api/v1/ping");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.True((DateTime.UtcNow - body.GetProperty("utcNow").GetDateTime()).Duration() < TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task A_till_with_no_credential_reads_NotEnrolled_against_a_healthy_server()
    {
        var probe = new ConnectivityProbe(new PlutusApiClient(_f.CreateClient()), new InMemoryCredentials());

        var status = await probe.CheckAsync();

        Assert.Equal(TillConnection.NotEnrolled, status.State);
        Assert.True(status.ServerReachable);
        Assert.False(status.IsOnline);
    }

    /// <summary>A till wired the way a real one is: a bootstrap client for the anonymous calls, a
    /// token provider that caches the 12h device token, and an authorised client on top.</summary>
    private static (PlutusApiClient Api, InMemoryCredentials Credentials) TillFor(HttpClient http, Guid deviceId, string secret)
    {
        var credentials = new InMemoryCredentials();
        credentials.Save(deviceId, secret);
        var tokens = new DeviceTokenProvider(new PlutusApiClient(http), credentials);
        return (new PlutusApiClient(http, tokens), credentials);
    }

    [Fact]
    public async Task An_enrolled_till_reads_Online()
    {
        var http = _f.CreateClient();
        var code = await ProvisionTillCodeAsync(http, "conn-online@acme.test");
        var enrolled = await new PlutusApiClient(http).EnrolAsync(code);

        var (api, credentials) = TillFor(http, enrolled.DeviceId, enrolled.ClientSecret);
        var status = await new ConnectivityProbe(api, credentials).CheckAsync();

        Assert.Equal(TillConnection.Online, status.State);
        Assert.True(status.IsOnline);
        Assert.Equal("Active", status.DeviceStatus);
    }

    [Fact]
    public async Task A_revoked_till_reads_Rejected_NOT_offline()
    {
        // ⚠ The whole point. The server is demonstrably up — the ping succeeded — and it will not
        // have this till. Calling that "offline" sends a shop to reboot its router while the fix is
        // one click in the portal.
        //
        // It also pins the only revocation signal that reaches a till: device tokens are bearer
        // tokens with no server-side denylist, so a revoked till keeps working on its cached token
        // for up to 12h unless something polls this route.
        var http = _f.CreateClient();
        var code = await ProvisionTillCodeAsync(http, "conn-revoked@acme.test");
        var enrolled = await new PlutusApiClient(http).EnrolAsync(code);
        var (api, credentials) = TillFor(http, enrolled.DeviceId, enrolled.ClientSecret);

        // Prove it was healthy first, so the assertion below is about revocation and nothing else.
        Assert.Equal(TillConnection.Online, (await new ConnectivityProbe(api, credentials).CheckAsync()).State);

        var portal = PlutusAppFactory.OperatorToken(PlutusPolicies.PortalTillsEnrol, enrolled.TenantId);
        using var revoke = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tills/{enrolled.TillId}/revoke");
        revoke.Headers.Authorization = new("Bearer", portal);
        Assert.True((await http.SendAsync(revoke)).IsSuccessStatusCode);

        var status = await new ConnectivityProbe(api, credentials).CheckAsync();

        Assert.Equal(TillConnection.Rejected, status.State);
        Assert.True(status.ServerReachable);
        Assert.False(status.IsOnline);
    }

    [Fact]
    public async Task A_credential_the_server_never_issued_reads_Rejected()
    {
        var http = _f.CreateClient();
        var (api, credentials) = TillFor(http, Guid.NewGuid(), "a-secret-this-server-never-issued");

        var status = await new ConnectivityProbe(api, credentials).CheckAsync();

        Assert.Equal(TillConnection.Rejected, status.State);
        Assert.True(status.ServerReachable);
        Assert.False(status.IsOnline);
    }
}
