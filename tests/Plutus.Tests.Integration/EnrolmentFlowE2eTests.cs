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

    private async Task<(string Code, Guid TillId, int StoreId)> ProvisionAsync(HttpClient c, string email)
    {
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);
        using var pReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tenants")
        { Content = JsonContent.Create(new { name = "WP4 " + email, plan = "standard", adminEmail = email, adminPassword = "S3cret!" }) };
        pReq.Headers.Authorization = new("Bearer", admin);
        var pBody = JsonDocument.Parse(await (await c.SendAsync(pReq)).Content.ReadAsStringAsync()).RootElement;
        var tenantId = pBody.GetProperty("tenantId").GetGuid();
        var storeId = pBody.GetProperty("storeId").GetInt32();

        var portal = PlutusAppFactory.OperatorToken(PlutusPolicies.PortalTillsEnrol, tenantId);
        using var tReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tills")
        { Content = JsonContent.Create(new { storeId, name = "WP4 till" }) };
        tReq.Headers.Authorization = new("Bearer", portal);
        var tBody = JsonDocument.Parse(await (await c.SendAsync(tReq)).Content.ReadAsStringAsync()).RootElement;
        return (tBody.GetProperty("enrolmentCode").GetString()!, tBody.GetProperty("tillId").GetGuid(), storeId);
    }

    [Fact]
    public async Task Enrolment_REFUSES_while_the_legacy_database_is_un_archived()
    {
        var http = _f.CreateClient();
        var (code, _, _) = await ProvisionAsync(http, "wp4a@acme.test");
        var creds = new SecureStorageStub();
        var flow = new EnrolmentFlow(_store, new PlutusApiClient(http), creds);

        // a live till: its old database is sitting right there, unarchived
        var legacy = Path.Combine(_tempDir, "Database.db");
        await File.WriteAllTextAsync(legacy, "a shop's sales history");

        var blocked = await Assert.ThrowsAsync<EnrolmentBlockedException>(
            () => flow.EnrolAsync("https://plutus.example", code, legacy));
        Assert.Contains("archive", blocked.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stranded", blocked.Message);

        // nothing was half-done: no credential, no identity, and the code is still unused
        Assert.Null(creds.DeviceId);
        Assert.Null(await _store.GetGuidMetaAsync(MetaKeys.TillId));

        // archive it, and the same code now works — the gate is a gate, not a wall
        Cutover.ArchiveLegacyDatabase(legacy, Path.Combine(_tempDir, "archive"), DateTime.UtcNow);
        await _store.SetMetaAsync(MetaKeys.LegacyArchivedAtUtc, DateTime.UtcNow.ToString("O"));
        var deviceId = await flow.EnrolAsync("https://plutus.example", code, legacy);
        Assert.NotEqual(Guid.Empty, deviceId);
    }

    [Fact]
    public async Task A_clean_install_enrols_stores_its_identity_and_survives_a_restart()
    {
        var http = _f.CreateClient();
        var (code, tillId, storeId) = await ProvisionAsync(http, "wp4b@acme.test");
        var creds = new SecureStorageStub();
        var api = new PlutusApiClient(http);
        var flow = new EnrolmentFlow(_store, api, creds);

        Assert.False(await flow.IsEnrolledAsync());
        Assert.Null(await flow.BlockedReasonAsync(legacyDatabasePath: null));   // nothing to archive

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
}
