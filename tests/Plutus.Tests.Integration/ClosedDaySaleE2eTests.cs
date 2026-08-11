using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// A Z-closed business day takes no more money — enforced by the PLATFORM, not only by the till.
///
/// ⚠⚠ Matt, 2026-08-11: *"I was able to make a sale with the till closed, but it doesn't look like
/// the sale was captured. I was also able to refund it."*
///
/// He was, and the platform ACCEPTED IT — 201 Recorded, onto a day whose takings had already been
/// counted and banked. The day-closed rule existed only on the cash-event path (`CashModule` 409s
/// every event after a ZClose); sales never asked.
///
/// ⚠ THE TILL NOW REFUSES IT TOO, and this test is the reason that is not enough on its own. The
/// till's gate protects the OPERATOR — it refuses before money is taken, basket intact. This one
/// protects the LEDGER, and a till cannot be the only thing enforcing a rule about the platform's
/// own books: an older build, a replayed queue, or a second device on the same till all reach the
/// endpoint without passing through that check.
///
/// ⚠ QUARANTINED (202), NOT REJECTED. The sale is real — somebody paid and left with the goods — so
/// it must not vanish. Quarantine keeps the money visible and puts it in front of a person.
/// </summary>
public class ClosedDaySaleE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public ClosedDaySaleE2eTests(PlutusAppFactory f) => _f = f;

    private sealed class Credentials : IDeviceCredentialStore
    {
        public Guid? DeviceId { get; private set; }
        public string ClientSecret { get; private set; }
        public void Save(Guid deviceId, string clientSecret) { DeviceId = deviceId; ClientSecret = clientSecret; }
        public void Clear() { DeviceId = null; ClientSecret = null; }
    }

    private static async Task<JsonElement> PostAsync(HttpClient c, string url, string token, object body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var res = await c.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task<(PlutusApiClient Api, Guid DeviceId, Guid BusinessId, Guid TenantId, int StoreId)>
        EnrolAsync(HttpClient http, string email)
    {
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);
        var tenant = await PostAsync(http, "/api/v1/tenants", admin,
            new { name = "Closed " + email, plan = "standard", adminEmail = email, adminPassword = "S3cret!" });
        var tenantId = tenant.GetProperty("tenantId").GetGuid();
        var storeId = tenant.GetProperty("storeId").GetInt32();

        var (api, deviceId) = await EnrolTillAsync(http, tenantId, storeId, "Closed-day till");
        var businessId = (await api.GetStoreInfoAsync(storeId))!.BusinessId!.Value;
        return (api, deviceId, businessId, tenantId, storeId);
    }

    /// <summary>
    /// Enrol ANOTHER till in an EXISTING tenant and store.
    ///
    /// ⚠ IT EXISTS BECAUSE A MUTATION CAUGHT A TEST PASSING FOR THE WRONG REASON. The "another till
    /// is unaffected" case originally enrolled a second TENANT — so deleting the `TillId` filter
    /// from the server's guard did not fail it, because the `TenantId` filter was doing all the
    /// work. It was proving tenant isolation, which was never in doubt, and saying nothing at all
    /// about tills.
    /// </summary>
    private static async Task<(PlutusApiClient Api, Guid DeviceId)> EnrolTillAsync(
        HttpClient http, Guid tenantId, int storeId, string name)
    {
        var portal = PlutusAppFactory.OperatorToken(PlutusPolicies.PortalTillsEnrol, tenantId);
        var till = await PostAsync(http, "/api/v1/tills", portal, new { storeId, name });

        var bootstrap = new PlutusApiClient(http);
        var enrolled = await bootstrap.EnrolAsync(till.GetProperty("enrolmentCode").GetString()!);
        var creds = new Credentials();
        creds.Save(enrolled.DeviceId, enrolled.ClientSecret);

        return (new PlutusApiClient(http, new DeviceTokenProvider(bootstrap, creds)), enrolled.DeviceId);
    }

    private static CashEventRequest XSnapshot(Guid deviceId, DateOnly day, long countedPence) => new()
    {
        EventId = Uuid7.New(),
        DeviceId = deviceId,
        Type = CashEventTypes.XSnapshot,
        BusinessDay = day,
        OccurredAtUtc = DateTime.UtcNow,
        AmountPence = 0,
        CountedPence = countedPence,
        Reason = "mid-shift count",
    };

    private static CashEventRequest ZClose(Guid deviceId, DateOnly day, long countedPence) => new()
    {
        EventId = Uuid7.New(),
        DeviceId = deviceId,
        Type = CashEventTypes.ZClose,
        BusinessDay = day,
        OccurredAtUtc = DateTime.UtcNow,
        AmountPence = 0,
        CountedPence = countedPence,
        Reason = "end of day",
    };

    private static IngestSaleRequest SaleFor(Guid deviceId, Guid businessId, DateOnly day, long seq)
    {
        var lines = new[] { new BasketLine(Guid.Empty, "5010001", "Comic", 1499, 1249, 1) };
        var totals = SaleAssembler.Total(lines);

        return SaleAssembler.Assemble(
            Uuid7.New(), deviceId, seq, businessId, lines,
            new[] { new IngestTender { TenderType = Tenders.Cash, AmountPence = totals.GrossPence } },
            day, DateTime.UtcNow);
    }

    [Fact]
    public async Task A_sale_on_an_OPEN_day_is_recorded()
    {
        // The control. Without it, a test that only proves "quarantined" would also pass if the
        // endpoint had simply stopped accepting anything.
        var http = _f.CreateClient();
        var (api, deviceId, businessId, _, _) = await EnrolAsync(http, "closedday1@acme.test");
        var day = new DateOnly(2026, 8, 11);

        var (status, body) = await api.PostSaleAsync(
            JsonSerializer.Serialize(SaleFor(deviceId, businessId, day, 1), PlutusApiClient.Json));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("recorded", body?.Status?.ToLowerInvariant());
    }

    [Fact]
    public async Task A_sale_AFTER_a_Z_CLOSE_is_QUARANTINED_not_recorded()
    {
        // ⚠⚠ THE ONE MATT FOUND. Before this, the second post came back 201 Recorded.
        var http = _f.CreateClient();
        var (api, deviceId, businessId, _, _) = await EnrolAsync(http, "closedday2@acme.test");
        var day = new DateOnly(2026, 8, 11);

        // A sale, then the day is closed with a Z read — the ordinary end of a trading day.
        var (openStatus, _) = await api.PostSaleAsync(
            JsonSerializer.Serialize(SaleFor(deviceId, businessId, day, 1), PlutusApiClient.Json));
        Assert.Equal(HttpStatusCode.Created, openStatus);

        var (zStatus, _) = await api.PostCashEventAsync(ZClose(deviceId, day, 1499));

        Assert.True(zStatus is HttpStatusCode.Created or HttpStatusCode.OK,
            $"the Z close should be accepted, got {zStatus}");

        // ⚠ Now the sale that used to be accepted.
        var (afterStatus, afterBody) = await api.PostSaleAsync(
            JsonSerializer.Serialize(SaleFor(deviceId, businessId, day, 2), PlutusApiClient.Json));

        Assert.Equal(HttpStatusCode.Accepted, afterStatus);
        Assert.Equal("quarantined", afterBody?.Status?.ToLowerInvariant());
    }

    [Fact]
    public async Task Closing_one_day_does_not_block_the_NEXT_day()
    {
        // ⚠ THE ONE THAT WOULD BRICK EVERY TILL EVERY MORNING. A shop closes every night; if the
        // guard were not per business day, no till would ever open again.
        var http = _f.CreateClient();
        var (api, deviceId, businessId, _, _) = await EnrolAsync(http, "closedday3@acme.test");
        var closed = new DateOnly(2026, 8, 11);

        await api.PostCashEventAsync(ZClose(deviceId, closed, 0));

        var (status, body) = await api.PostSaleAsync(
            JsonSerializer.Serialize(SaleFor(deviceId, businessId, closed.AddDays(1), 3), PlutusApiClient.Json));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("recorded", body?.Status?.ToLowerInvariant());
    }

    [Fact]
    public async Task An_X_SNAPSHOT_does_NOT_close_the_day()
    {
        // ⚠ AN X IS A MID-SHIFT COUNT. If the server treated it as a close, a manager checking the
        // drawer at lunchtime would stop the shop selling for the rest of the day — and every sale
        // after it would quarantine. Added because a mutation that counted X as a close broke
        // nothing: no test reached it.
        var http = _f.CreateClient();
        var (api, deviceId, businessId, _, _) = await EnrolAsync(http, "closedday5@acme.test");
        var day = new DateOnly(2026, 8, 11);

        await api.PostCashEventAsync(XSnapshot(deviceId, day, 1499));

        var (status, body) = await api.PostSaleAsync(
            JsonSerializer.Serialize(SaleFor(deviceId, businessId, day, 1), PlutusApiClient.Json));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("recorded", body?.Status?.ToLowerInvariant());
    }

    [Fact]
    public async Task Another_TILL_IN_THE_SAME_TENANT_that_has_not_closed_is_unaffected()
    {
        // ⚠ TWO TILLS IN ONE TENANT — see `EnrolTillAsync`. The first version of this test used two
        // TENANTS, so the tenant filter did all the work and the till filter was never exercised.
        // A mutation deleting `e.TillId == tillId` passed it happily.
        var http = _f.CreateClient();
        var (apiA, deviceA, businessId, tenantId, storeId) = await EnrolAsync(http, "closedday6@acme.test");
        var (apiB, deviceB) = await EnrolTillAsync(http, tenantId, storeId, "Second counter");
        var day = new DateOnly(2026, 8, 11);

        // Till A cashes up. Till B is still trading.
        await apiA.PostCashEventAsync(ZClose(deviceA, day, 0));

        var (statusB, bodyB) = await apiB.PostSaleAsync(
            JsonSerializer.Serialize(SaleFor(deviceB, businessId, day, 1), PlutusApiClient.Json));

        Assert.Equal(HttpStatusCode.Created, statusB);
        Assert.Equal("recorded", bodyB?.Status?.ToLowerInvariant());

        // ⚠ And A really is closed — otherwise this test would pass with the guard switched off.
        var (statusA, bodyA) = await apiA.PostSaleAsync(
            JsonSerializer.Serialize(SaleFor(deviceA, businessId, day, 2), PlutusApiClient.Json));

        Assert.Equal(HttpStatusCode.Accepted, statusA);
        Assert.Equal("quarantined", bodyA?.Status?.ToLowerInvariant());
    }

    [Fact]
    public async Task Two_separate_TENANTS_are_also_unaffected()
    {
        // ⚠ The guard is per TILL as well as per day. One till cashing up must not stop the counter
        // next to it trading — and `TillId` is server-authoritative, so a till cannot claim to be
        // another one to get round this.
        var http = _f.CreateClient();
        var (apiA, deviceA, _, _, _) = await EnrolAsync(http, "closedday4a@acme.test");
        var (apiB, deviceB, businessB, _, _) = await EnrolAsync(http, "closedday4b@acme.test");
        var day = new DateOnly(2026, 8, 11);

        await apiA.PostCashEventAsync(ZClose(deviceA, day, 0));

        var (statusB, bodyB) = await apiB.PostSaleAsync(
            JsonSerializer.Serialize(SaleFor(deviceB, businessB, day, 1), PlutusApiClient.Json));

        Assert.Equal(HttpStatusCode.Created, statusB);
        Assert.Equal("recorded", bodyB?.Status?.ToLowerInvariant());
    }
}
