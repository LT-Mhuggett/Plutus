using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// Notification framework (17.3 config layer): the operator configures a provider per channel
/// (secrets write-only), a test send runs in SIMULATED mode until an adapter is wired and lands in
/// the delivery ledger, a disabled channel sends nothing, and everything is platform-admin gated.
/// </summary>
public class NotificationConfigE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public NotificationConfigE2eTests(PlutusAppFactory f) => _f = f;

    private HttpRequestMessage Admin(HttpMethod m, string url, object body = null)
    {
        var req = new HttpRequestMessage(m, url) { Content = body == null ? null : JsonContent.Create(body) };
        req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin));
        return req;
    }

    [Fact]
    public async Task Catalogue_and_gate()
    {
        var client = _f.CreateClient();
        // non-admin → 403
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/notifications/catalogue"))
        {
            req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken("pos.sell"));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        }
        // admin → 200 + carries the providers
        using var areq = Admin(HttpMethod.Get, "/api/v1/platform/notifications/catalogue");
        var resp = await client.SendAsync(areq);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("postmark", body);
        Assert.Contains("\"secret\":true", body); // a secret field is flagged
    }

    [Fact]
    public async Task Configure_secret_is_write_only_and_test_send_is_simulated_and_logged()
    {
        var client = _f.CreateClient();
        var tenant = Guid.NewGuid();

        // configure Postmark (a secret serverToken + a from default), enabled
        var cfg = new SetBody
        {
            channel = 0, provider = "postmark", enabled = true,
            config = new Dictionary<string, string> { ["serverToken"] = "super-secret-token", ["fromDefault"] = "no-reply@shop.example" },
        };
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(Admin(HttpMethod.Put, "/api/v1/platform/notifications/config", cfg))).StatusCode);

        // read back: the secret is redacted (never returned in the clear), the non-secret is visible
        var getResp = await client.SendAsync(Admin(HttpMethod.Get, "/api/v1/platform/notifications/config"));
        var getBody = await getResp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("super-secret-token", getBody);
        Assert.Contains("no-reply@shop.example", getBody);
        Assert.Contains("__set__", getBody); // sentinel marks the stored secret

        // test send → accepted in SIMULATED mode (no adapter wired), lands in the ledger
        var test = new { channel = 0, tenantId = tenant, to = "buyer@example.com" };
        var testResp = await client.SendAsync(Admin(HttpMethod.Post, "/api/v1/platform/notifications/test", test));
        Assert.Equal(HttpStatusCode.OK, testResp.StatusCode);
        var testBody = await testResp.Content.ReadAsStringAsync();
        Assert.Contains("SIMULATED", testBody);

        var eventsBody = await (await client.SendAsync(Admin(HttpMethod.Get, "/api/v1/platform/notifications/events"))).Content.ReadAsStringAsync();
        Assert.Contains("buyer@example.com", eventsBody);
    }

    [Fact]
    public async Task Disabled_channel_sends_nothing()
    {
        var client = _f.CreateClient();
        // disable channel 1 (sms) explicitly
        var cfg = new SetBody { channel = 1, provider = "none", enabled = false, config = new() };
        await client.SendAsync(Admin(HttpMethod.Put, "/api/v1/platform/notifications/config", cfg));

        var test = new { channel = 1, tenantId = Guid.NewGuid(), to = "07000000000" };
        var resp = await client.SendAsync(Admin(HttpMethod.Post, "/api/v1/platform/notifications/test", test));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("disabled", (await resp.Content.ReadAsStringAsync()).ToLowerInvariant());
    }

    private sealed class SetBody
    {
        public int channel { get; set; }
        public string provider { get; set; }
        public bool enabled { get; set; }
        public Dictionary<string, string> config { get; set; }
    }
}
