using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Client.Core;
using Plutus.Client.Storage;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// MAUI retrofit WP4 — Definition of Done for enrolment and device identity, against the real
/// endpoints.
///
/// The case that matters most is the one that refuses: a live till holds real sales in its old
/// database, and enrolling before that file is archived strands a shop's history on a machine
/// that now looks empty. That has to fail loudly, because it fails silently otherwise.
/// </summary>
public class EnrolmentFlowE2eTests : IClassFixture<PlutusAppFactory>, IAsyncLifetime
{
    private readonly PlutusAppFactory _f;
    public EnrolmentFlowE2eTests(PlutusAppFactory f) => _f = f;

    private SqliteConnection _conn = null!;
    private TillDbContext _db = null!;
    private TillStore _store = null!;
    private string _tempDir = null!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        await _conn.OpenAsync();
        _db = new TillDbContext(new DbContextOptionsBuilder<TillDbContext>().UseSqlite(_conn).Options);
        await _db.EnsureReadyAsync();
        _store = new TillStore(_db);
        _tempDir = Path.Combine(Path.GetTempPath(), "plutus-wp4-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>Stands in for platform secure storage — the point being that it is NOT the SQLite
    /// file, which support copies off machines routinely.</summary>
    private sealed class SecureStorageStub : IDeviceCredentialStore
    {
        public Guid? DeviceId { get; private set; }
        public string? ClientSecret { get; private set; }
        public void Save(Guid deviceId, string clientSecret) { DeviceId = deviceId; ClientSecret = clientSecret; }
        public void Clear() { DeviceId = null; ClientSecret = null; }
    }

    /// <summary>
    /// ⚠ Reads the body ONLY after asserting the status. It used to parse straight into
    /// <c>JsonDocument</c>, so any server-side failure arrived as
    /// <c>JsonReaderException: 'M' is an invalid start of a value</c> — the first character of an
    /// error page — which says nothing about what actually broke. A test helper that hides the
    /// cause of its own failure costs more time than the test saves.
    /// </summary>
    private static async Task<JsonElement> PostAsync(HttpClient c, string url, string token, object body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        req.Headers.Authorization = new("Bearer", token);
        var res = await c.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode,
            $"POST {url} returned {(int)res.StatusCode} {res.StatusCode}. Body: {text}");
        return JsonDocument.Parse(text).RootElement;
    }

    private async Task<(string Code, Guid TillId, int StoreId)> ProvisionAsync(HttpClient c, string email)
    {
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);
        var pBody = await PostAsync(c, "/api/v1/tenants", admin,
            new { name = "WP4 " + email, plan = "standard", adminEmail = email, adminPassword = "S3cret!" });
        var tenantId = pBody.GetProperty("tenantId").GetGuid();
        var storeId = pBody.GetProperty("storeId").GetInt32();

        var portal = PlutusAppFactory.OperatorToken(PlutusPolicies.PortalTillsEnrol, tenantId);
        var tBody = await PostAsync(c, "/api/v1/tills", portal, new { storeId, name = "WP4 till" });
        return (tBody.GetProperty("enrolmentCode").GetString()!, tBody.GetProperty("tillId").GetGuid(), storeId);
    }

    // ⚠⚠ `Enrolment_REFUSES_while_the_legacy_database_is_un_archived` WAS DELETED WITH THE GATE —
    // L1, 2026-08-23. It asserted that a till holding an un-archived legacy database could not enrol,
    // which was the protection for a shop migrating off NatApp: enrol first and your history is
    // stranded on that machine.
    //
    // ⚠ The gate had already been inert — its one production caller passed `null` — and removing it
    // is Matt's decision of 2026-08-10, on the basis that no such migration is planned. The test is
    // named here rather than quietly dropped, because a deleted test is a deleted requirement and the
    // next person planning a NatApp migration needs to know this one existed.
    [Fact]
    public async Task A_clean_install_enrols_stores_its_identity_and_survives_a_restart()
    {
        var http = _f.CreateClient();
        var (code, tillId, storeId) = await ProvisionAsync(http, "wp4b@acme.test");
        var creds = new SecureStorageStub();
        var api = new PlutusApiClient(http);
        var flow = new EnrolmentFlow(_store, api, creds);

        Assert.False(await flow.IsEnrolledAsync());
        // ⚠ The archive gate assertion went with `BlockedReasonAsync` (L1, 2026-08-23).

        var deviceId = await flow.EnrolAsync("https://plutus.example", code);

        Assert.True(await flow.IsEnrolledAsync());
        Assert.Equal(tillId, await _store.GetGuidMetaAsync(MetaKeys.TillId));
        Assert.Equal("https://plutus.example", await _store.GetMetaAsync(MetaKeys.ServerUrl));

        // ⚠ the secret lives in secure storage, NEVER the database file
        Assert.NotNull(creds.ClientSecret);
        var everyMetaValue = string.Join("|", await _db.Meta.Select(m => m.Value).ToListAsync());
        Assert.DoesNotContain(creds.ClientSecret!, everyMetaValue);

        // placement: which store, and the LEGACY BusinessId that seeds item-id derivation
        var tokens = new DeviceTokenProvider(api, creds);
        var authed = new PlutusApiClient(http, tokens);
        await new EnrolmentFlow(_store, authed, creds).RefreshPlacementAsync();

        Assert.Equal(storeId, await _store.GetIntMetaAsync(MetaKeys.StoreId));
        var businessId = await _store.GetGuidMetaAsync(MetaKeys.BusinessId);
        Assert.NotNull(businessId);
        Assert.NotEqual(Guid.Empty, businessId!.Value);
        // and it is NOT the tenant id — the silent-corruption trap
        Assert.NotEqual(await _store.GetGuidMetaAsync(MetaKeys.TenantId), businessId);

        // "restart": a fresh context over the same file is still enrolled, no re-prompt
        await using var reopened = new TillDbContext(new DbContextOptionsBuilder<TillDbContext>().UseSqlite(_conn).Options);
        Assert.Equal(tillId, await new TillStore(reopened).GetGuidMetaAsync(MetaKeys.TillId));
    }

    /// <summary>
    /// ⚠ PLACEMENT IS REFRESHED ON EVERY START, so it must be idempotent and it must FOLLOW a move.
    ///
    /// Cutover step 4 calls this on each launch for two reasons: a till can be moved between stores
    /// in the portal — and its prices, receipts and themes have to follow it — and every till
    /// enrolled before 2026-08-09 was never told its store at all, because MAUI enrolled by calling
    /// the API directly and bypassing this flow entirely. Those tills self-heal on the next start
    /// rather than needing a re-enrolment, which would mint a second device row for a machine that
    /// is already correctly paired.
    ///
    /// ⚠ A refresh that CANNOT reach the server must leave the last known placement alone. A till
    /// whose broadband is down still knows which shop it is in; blanking that would take a working
    /// offline till and make it unable to price anything.
    /// </summary>
    [Fact]
    public async Task Placement_refresh_is_idempotent_and_never_blanks_what_it_already_knew()
    {
        var http = _f.CreateClient();
        var (code, tillId, storeId) = await ProvisionAsync(http, "wp4d@acme.test");
        var creds = new SecureStorageStub();
        var api = new PlutusApiClient(http);

        await new EnrolmentFlow(_store, api, creds).EnrolAsync("https://plutus.example", code);

        var authed = new PlutusApiClient(http, new DeviceTokenProvider(api, creds));
        var flow = new EnrolmentFlow(_store, authed, creds);

        await flow.RefreshPlacementAsync();
        var storeAfterFirst = await _store.GetIntMetaAsync(MetaKeys.StoreId);
        var businessAfterFirst = await _store.GetGuidMetaAsync(MetaKeys.BusinessId);
        Assert.Equal(storeId, storeAfterFirst);
        Assert.NotNull(businessAfterFirst);

        // Idempotent: running it again on every start must not churn the values.
        await flow.RefreshPlacementAsync();
        await flow.RefreshPlacementAsync();
        Assert.Equal(storeAfterFirst, await _store.GetIntMetaAsync(MetaKeys.StoreId));
        Assert.Equal(businessAfterFirst, await _store.GetGuidMetaAsync(MetaKeys.BusinessId));
        Assert.Equal(tillId, await _store.GetGuidMetaAsync(MetaKeys.TillId));

        // ⚠ And an UNREACHABLE server leaves the known placement standing. A client with no token
        // provider gets 401s from every placement call — the shape of an offline till.
        await new EnrolmentFlow(_store, new PlutusApiClient(http), creds).RefreshPlacementAsync();
        Assert.Equal(storeAfterFirst, await _store.GetIntMetaAsync(MetaKeys.StoreId));
        Assert.Equal(businessAfterFirst, await _store.GetGuidMetaAsync(MetaKeys.BusinessId));
    }

    [Fact]
    public async Task A_reused_code_is_a_message_not_a_crash_and_a_revoked_device_keeps_its_sales()
    {
        var http = _f.CreateClient();
        var (code, _, _) = await ProvisionAsync(http, "wp4c@acme.test");
        var creds = new SecureStorageStub();
        var flow = new EnrolmentFlow(_store, new PlutusApiClient(http), creds);

        await flow.EnrolAsync("https://plutus.example", code);

        var reuse = await Assert.ThrowsAsync<EnrolmentFailedException>(
            () => flow.EnrolAsync("https://plutus.example", code));
        Assert.Equal(HttpStatusCode.Gone, reuse.Status);
        Assert.Contains("already been used", reuse.Message);

        // a sale taken before the device was revoked is money that may not have synced —
        // forgetting the credential must NEVER discard it
        await _store.CommitSaleAsync(new Contracts.Client.IngestSaleRequest
        {
            SaleId = Uuid7.New(), BusinessDay = new DateOnly(2026, 8, 7), OccurredAtUtc = DateTime.UtcNow,
            GrossPence = 600, VatPence = 100,
            Lines = { new Contracts.Client.IngestLine { Qty = 1, UnitPricePence = 600, LineGrossPence = 600, VatRateBp = 2000, VatAmountPence = 100 } },
        });

        await flow.ForgetDeviceAsync();

        Assert.Null(creds.DeviceId);
        Assert.Equal(1, await _store.CountAsync(OutboxStatus.Pending));   // the sale survived
    }

    /// <summary>
    /// ⚠ FORGETTING A TILL MUST CLEAR ITS POSTING, not just its credential.
    ///
    /// `ForgetDeviceAsync` used to null `MetaKeys.DeviceId` alone, leaving TillId, TenantId, StoreId
    /// and BusinessId behind — so a "forgotten" till went on answering as the till it had just been
    /// un-enrolled from. `TillPlacement` kept handing out the old TillId and StoreId, receipts kept
    /// the old store's address, and item ids kept deriving from the old BusinessId. Enrolling it
    /// somewhere else then produced a device carrying two identities at once.
    ///
    /// ⚠ `ServerUrl` deliberately SURVIVES — it is how the operator reaches the portal to enrol
    /// again. Wiping it turns "forget this till" into "and now type the address in from memory".
    /// </summary>
    [Fact]
    public async Task Forgetting_a_till_clears_its_PLACEMENT_but_keeps_the_server_address()
    {
        var http = _f.CreateClient();
        var (code, _, _) = await ProvisionAsync(http, "forget-placement@acme.test");
        var creds = new SecureStorageStub();
        var api = new PlutusApiClient(http);
        var flow = new EnrolmentFlow(_store, api, creds);

        await flow.EnrolAsync("https://plutus.example", code);

        // Placement needs a DEVICE token — the same shape as the clean-install test above.
        var authed = new PlutusApiClient(http, new DeviceTokenProvider(api, creds));
        await new EnrolmentFlow(_store, authed, creds).RefreshPlacementAsync();

        // Precondition: enrolment + placement recorded a full identity.
        Assert.NotNull(await _store.GetMetaAsync(MetaKeys.TillId));
        Assert.NotNull(await _store.GetMetaAsync(MetaKeys.ServerUrl));

        await flow.ForgetDeviceAsync();

        Assert.Null(creds.DeviceId);
        Assert.Null(await _store.GetMetaAsync(MetaKeys.DeviceId));
        Assert.Null(await _store.GetMetaAsync(MetaKeys.TillId));
        Assert.Null(await _store.GetMetaAsync(MetaKeys.TenantId));
        Assert.Null(await _store.GetMetaAsync(MetaKeys.StoreId));
        Assert.Null(await _store.GetMetaAsync(MetaKeys.BusinessId));

        // ⚠ The way back in.
        Assert.NotNull(await _store.GetMetaAsync(MetaKeys.ServerUrl));
    }

    // ⚠ This test asserted the gate was INTACT but not asked. The gate went with L1 (2026-08-23),
    // so both halves of its assertion are gone — see the note further up naming the deleted rule.
}
