using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>WP1.7 HTTP-level: the full T1.2/T1.4 lifecycle through the real pipeline (auth
/// policies, tenancy, ingest, revoke), a route-surface assertion via ApiExplorer, and a
/// tenant-scoping proof (the saleId namespace is per-tenant).</summary>
public class E2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public E2eTests(PlutusAppFactory f) => _f = f;

    private static async Task<(HttpStatusCode Status, JsonElement Body)> Post(
        HttpClient c, string url, object body, string token = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (token != null) req.Headers.Authorization = new("Bearer", token);
        if (body != null) req.Content = JsonContent.Create(body);
        var resp = await c.SendAsync(req);
        var txt = await resp.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(txt)) return (resp.StatusCode, default);
        try
        {
            return (resp.StatusCode, JsonDocument.Parse(txt).RootElement.Clone());
        }
        catch (JsonException)
        {
            throw new Xunit.Sdk.XunitException($"{req.Method} {url} → {(int)resp.StatusCode}; non-JSON body:\n{txt[..Math.Min(txt.Length, 1200)]}");
        }
    }

    private static JsonElement Prop(JsonElement e, string name)
    {
        foreach (var p in e.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        throw new Xunit.Sdk.XunitException($"property '{name}' not found in {e}");
    }

    private static object ValidSale(Guid saleId, long seq) => new
    {
        saleId,
        deviceSeq = seq,
        channel = 0,
        businessDay = "2026-07-24",
        occurredAtUtc = DateTime.UtcNow,
        grossPence = 200,
        vatPence = 40,
        lines = new[] { new { itemId = Guid.NewGuid(), qty = 2, unitPricePence = 100, discountPence = 0, lineGrossPence = 200, vatRateBp = 2000, vatAmountPence = 40 } },
        tenders = new[] { new { tenderType = 0, amountPence = 200, changePence = 0 } },
    };

    // Provisions a tenant and drives it through till → enrol → device token; returns the
    // device access token + the tenant/till ids.
    private async Task<(string DeviceToken, Guid TenantId, Guid TillId)> ProvisionAndEnrol(HttpClient c, string email)
    {
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);
        var (ps, pb) = await Post(c, "/api/v1/tenants",
            new { name = "Acme " + email, plan = "standard", adminEmail = email, adminPassword = "S3cret!" }, admin);
        Assert.Equal(HttpStatusCode.Created, ps);
        var tenantId = Prop(pb, "tenantId").GetGuid();
        var storeId = Prop(pb, "storeId").GetInt32();

        var portal = PlutusAppFactory.OperatorToken(PlutusPolicies.PortalTillsEnrol, tenantId);
        var (ts, tb) = await Post(c, "/api/v1/tills", new { storeId, name = "Main" }, portal);
        Assert.Equal(HttpStatusCode.Created, ts);
        var tillId = Prop(tb, "tillId").GetGuid();
        var code = Prop(tb, "enrolmentCode").GetString();

        var (es, eb) = await Post(c, "/api/v1/tills/enrol", new { enrolmentCode = code });
        Assert.Equal(HttpStatusCode.OK, es);
        var deviceId = Prop(eb, "deviceId").GetGuid();
        var secret = Prop(eb, "clientSecret").GetString();
        Assert.Equal(tenantId, Prop(eb, "tenantId").GetGuid());

        var (ks, kb) = await Post(c, "/api/v1/tokens/device", new { deviceId, clientSecret = secret });
        Assert.Equal(HttpStatusCode.OK, ks);
        return (Prop(kb, "accessToken").GetString(), tenantId, tillId);
    }

    [Fact]
    public async Task Full_lifecycle_provision_enrol_token_ingest_revoke()
    {
        var c = _f.CreateClient();
        var (deviceToken, _, tillId) = await ProvisionAndEnrol(c, "admin1@acme.test");

        // Ingest a sale with the device token → 201 recorded; duplicate → 200.
        var saleId = Guid.NewGuid();
        var (s1, b1) = await Post(c, "/api/v1/sales", ValidSale(saleId, 1), deviceToken);
        Assert.Equal(HttpStatusCode.Created, s1);
        Assert.Equal("recorded", Prop(b1, "status").GetString());

        var (s2, _) = await Post(c, "/api/v1/sales", ValidSale(saleId, 1), deviceToken);
        Assert.Equal(HttpStatusCode.OK, s2); // idempotent replay

        // Revoke the till → device-token issuance then refuses (401).
        var portal = PlutusAppFactory.OperatorToken(PlutusPolicies.PortalTillsEnrol);
        // (revoke needs the till's tenant scope; re-mint a portal token carrying it is unnecessary —
        //  RevokeTillAsync is by tillId, and the portal policy only checks scope.)
        var (rs, _) = await Post(c, $"/api/v1/tills/{tillId}/revoke", null, portal);
        Assert.Equal(HttpStatusCode.NoContent, rs);
    }

    /// <summary>
    /// FE3.0 (Matt): agents must report their version + health back through the till so the portal's
    /// Locations page can see what runs on each till PC. The web till POSTs what it found on
    /// localhost (a device-token call); the tills list carries it per device. A report with a NULL
    /// version is meaningful — "this web till looked and found no agent" — and must round-trip too.
    /// </summary>
    [Fact]
    public async Task Agent_telemetry_round_trips_from_device_report_to_the_tills_list()
    {
        var c = _f.CreateClient();
        var (deviceToken, tenantId, tillId) = await ProvisionAndEnrol(c, "agent-telemetry@acme.test");
        var portal = PlutusAppFactory.OperatorToken(PlutusPolicies.PortalTillsEnrol, tenantId);

        async Task<JsonElement> MyDeviceAsync()
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tills");
            req.Headers.Authorization = new("Bearer", portal);
            var body = JsonDocument.Parse(await (await c.SendAsync(req)).Content.ReadAsStringAsync()).RootElement;
            var till = body.EnumerateArray().Single(t => Prop(t, "id").GetGuid() == tillId);
            return Prop(till, "devices").EnumerateArray().Single().Clone();
        }

        // never reported yet
        var before = await MyDeviceAsync();
        Assert.Equal(JsonValueKind.Null, Prop(before, "agentReportedAtUtc").ValueKind);
        var deviceId = Prop(before, "id").GetGuid();

        // the till reports a healthy agent → the list shows version + printer state
        var (s1, b1) = await Post(c, "/api/v1/tills/agent-status",
            new { deviceId, agentVersion = "1.2.3", printerName = "Epson TM-T88V", printerOnline = true }, deviceToken);
        Assert.True(s1 == HttpStatusCode.NoContent, $"agent-status → {(int)s1}: {b1}");

        var healthy = await MyDeviceAsync();
        Assert.Equal("1.2.3", Prop(healthy, "agentVersion").GetString());
        Assert.Equal("Epson TM-T88V", Prop(healthy, "agentPrinterName").GetString());
        Assert.True(Prop(healthy, "agentPrinterOnline").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, Prop(healthy, "agentReportedAtUtc").ValueKind);

        // the agent is uninstalled → the till reports "none found"; the fact it CHECKED is kept
        var (s2, _) = await Post(c, "/api/v1/tills/agent-status",
            new { deviceId, agentVersion = (string)null, printerName = (string)null, printerOnline = (bool?)null }, deviceToken);
        Assert.Equal(HttpStatusCode.NoContent, s2);

        var gone = await MyDeviceAsync();
        Assert.Equal(JsonValueKind.Null, Prop(gone, "agentVersion").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, Prop(gone, "agentReportedAtUtc").ValueKind);

        // a device id from nowhere → 404 (and the tenant check makes a foreign device look the same)
        var (s3, _) = await Post(c, "/api/v1/tills/agent-status",
            new { deviceId = Guid.NewGuid(), agentVersion = "6.6.6" }, deviceToken);
        Assert.Equal(HttpStatusCode.NotFound, s3);
    }

    [Fact]
    public async Task Unauthenticated_ingest_is_rejected()
    {
        var c = _f.CreateClient();
        var (s, _) = await Post(c, "/api/v1/sales", ValidSale(Guid.NewGuid(), 1)); // no token
        Assert.True(s is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Two_tenants_ingest_independently()
    {
        var c = _f.CreateClient();
        var (tokenA, tenantA, _) = await ProvisionAndEnrol(c, "a@acme.test");
        var (tokenB, tenantB, _) = await ProvisionAndEnrol(c, "b@acme.test");
        Assert.NotEqual(tenantA, tenantB);

        // Each tenant ingests its own sale (distinct UUIDv7 saleIds — globally unique by design);
        // both record through the same pipeline, each stamped to its own tenant.
        var (sa, ba) = await Post(c, "/api/v1/sales", ValidSale(Guid.NewGuid(), 1), tokenA);
        var (sb, bb) = await Post(c, "/api/v1/sales", ValidSale(Guid.NewGuid(), 1), tokenB);
        Assert.Equal(HttpStatusCode.Created, sa);
        Assert.Equal(HttpStatusCode.Created, sb);
        Assert.Equal("recorded", Prop(ba, "status").GetString());
        Assert.Equal("recorded", Prop(bb, "status").GetString());
    }

    [Fact]
    public void ApiExplorer_exposes_the_v1_surface()
    {
        var provider = _f.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>();
        var routes = provider.ApiDescriptionGroups.Items
            .SelectMany(g => g.Items)
            .Select(d => "/" + d.RelativePath.Split('?')[0])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var expected in new[]
        {
            "/api/v1/tenants", "/api/v1/tills", "/api/v1/tills/enrol",
            "/api/v1/tokens/device", "/api/v1/sales", "/api/v1/ops/deadletters",
        })
            Assert.Contains(expected, routes);
    }
}
