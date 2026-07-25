using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.SharedKernel;
using Plutus.Tenancy;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>WP1.2 enrolment lifecycle: create-till → enrol → device token → revoke, plus the
/// reused/expired-code 410 paths and revoked-device 401. Runs the real MySqlDbContext model on
/// SQLite in-memory (the tenancy tables come from OnModelCreating).</summary>
public class EnrolmentServiceTests
{
    private const string Secret = "unit-test-device-secret";
    private static readonly EnrolmentOptions Opts = new() { DeviceTokenSecret = Secret };

    private static MySqlDbContext Ctx(SqliteConnection conn, Guid tenant, string user = "test")
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(tenant)) { CurrentUser = user };

    private static SqliteConnection OpenDb(Guid tenant)
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var ctx = Ctx(conn, tenant);
        ctx.Database.EnsureCreated();
        return conn;
    }

    private static EnrolmentCode SeedCode(SqliteConnection conn, Guid tenant, Guid tillId,
        string code, DateTime? expires = null, DateTime? used = null)
    {
        using var ctx = Ctx(conn, tenant);
        var ec = new EnrolmentCode
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            TillId = tillId,
            CodeHash = CompactToken.Sha256(Crockford32.Normalise(code)),
            ExpiresAtUtc = expires ?? DateTime.UtcNow.AddHours(48),
            UsedAtUtc = used,
            CreatedAtUtc = DateTime.UtcNow,
        };
        ctx.EnrolmentCodes.Add(ec);
        ctx.SaveChanges();
        return ec;
    }

    [Fact]
    public async Task Enrol_then_token_then_revoke_lifecycle()
    {
        var tenant = Guid.NewGuid();
        var tillId = Guid.NewGuid();
        using var conn = OpenDb(tenant);
        SeedCode(conn, tenant, tillId, "ABCD2345");

        // Enrol → device + one-time secret.
        EnrolResult enrol;
        using (var ctx = Ctx(conn, WellKnownTenants.Kapow)) // anon context
            enrol = await new EnrolmentService(ctx, Opts).EnrolAsync("abcd2345" /*lower, normalised*/, "enrol");

        Assert.Equal(tillId, enrol.TillId);
        Assert.Equal(tenant, enrol.TenantId);
        Assert.False(string.IsNullOrWhiteSpace(enrol.ClientSecret));

        // Device token carries tid/did/scope.
        DeviceTokenResult tok;
        using (var ctx = Ctx(conn, WellKnownTenants.Kapow))
            tok = await new EnrolmentService(ctx, Opts).IssueDeviceTokenAsync(enrol.DeviceId, enrol.ClientSecret);

        var payload = CompactToken.Validate(tok.AccessToken, Secret);
        Assert.NotNull(payload);
        using (var doc = JsonDocument.Parse(payload!))
        {
            Assert.Equal(tenant.ToString(), doc.RootElement.GetProperty("tid").GetString());
            Assert.Equal(enrol.DeviceId.ToString(), doc.RootElement.GetProperty("did").GetString());
            Assert.Equal("device", doc.RootElement.GetProperty("scope").GetString());
        }

        // Revoke → token issuance refuses (401).
        using (var ctx = Ctx(conn, tenant))
            Assert.Equal(1, await new EnrolmentService(ctx, Opts).RevokeTillAsync(tillId, "portal"));

        using (var ctx = Ctx(conn, WellKnownTenants.Kapow))
        {
            var ex = await Assert.ThrowsAsync<EnrolmentException>(() =>
                new EnrolmentService(ctx, Opts).IssueDeviceTokenAsync(enrol.DeviceId, enrol.ClientSecret));
            Assert.Equal(401, ex.StatusCode);
        }
    }

    [Fact]
    public async Task Wrong_secret_is_401()
    {
        var tenant = Guid.NewGuid();
        using var conn = OpenDb(tenant);
        SeedCode(conn, tenant, Guid.NewGuid(), "SECRET22");

        EnrolResult enrol;
        using (var ctx = Ctx(conn, WellKnownTenants.Kapow))
            enrol = await new EnrolmentService(ctx, Opts).EnrolAsync("SECRET22", "enrol");

        using var c2 = Ctx(conn, WellKnownTenants.Kapow);
        var ex = await Assert.ThrowsAsync<EnrolmentException>(() =>
            new EnrolmentService(c2, Opts).IssueDeviceTokenAsync(enrol.DeviceId, "not-the-secret"));
        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task Reused_expired_and_unknown_codes_are_410()
    {
        var tenant = Guid.NewGuid();
        using var conn = OpenDb(tenant);
        SeedCode(conn, tenant, Guid.NewGuid(), "USED2222", used: DateTime.UtcNow);
        SeedCode(conn, tenant, Guid.NewGuid(), "OLD33333", expires: DateTime.UtcNow.AddHours(-1));

        foreach (var code in new[] { "USED2222", "OLD33333", "NOSUCH00" })
        {
            using var ctx = Ctx(conn, WellKnownTenants.Kapow);
            var ex = await Assert.ThrowsAsync<EnrolmentException>(() =>
                new EnrolmentService(ctx, Opts).EnrolAsync(code, "enrol"));
            Assert.Equal(410, ex.StatusCode);
        }
    }

    [Fact]
    public async Task CreateTill_persists_till_and_stores_only_the_code_hash()
    {
        var tenant = Guid.NewGuid();
        using var conn = OpenDb(tenant);

        int storeId;
        using (var ctx = Ctx(conn, tenant, "seed"))
        {
            var biz = new Business { Id = Guid.NewGuid(), Name = "Kapow", NameAbbr = "KAP", VatIN = "GB000" };
            ctx.Business.Add(biz);
            ctx.SaveChanges();
            var store = new Store
            {
                BusinessId = biz.Id, ContactNumber = "0", PostCode = "AB1 2CD",
                AdLine1 = "1 High St", AdLine2 = "", City = "Town", Country = "UK",
            };
            ctx.Stores.Add(store);
            ctx.SaveChanges();
            storeId = store.Id;
        }

        CreateTillResult res;
        using (var ctx = Ctx(conn, tenant, "portal"))
            res = await new EnrolmentService(ctx, Opts).CreateTillAsync(tenant, storeId, "Main", "portal");

        Assert.Equal(8, res.EnrolmentCode.Length);
        Assert.True(res.ExpiresAtUtc > DateTime.UtcNow);

        using (var ctx = Ctx(conn, tenant))
        {
            Assert.True(await ctx.Till.AnyAsync(t => t.Id == res.TillId));
            var hash = CompactToken.Sha256(Crockford32.Normalise(res.EnrolmentCode));
            var stored = await ctx.EnrolmentCodes.FirstAsync(e => e.TillId == res.TillId);
            Assert.Equal(hash, stored.CodeHash); // plaintext never persisted
            // WP11.1: the name is persisted in TillDetails (the legacy Till POCO has no Name).
            var details = await ctx.TillDetails.FirstAsync(t => t.TillId == res.TillId);
            Assert.Equal("Main", details.Name);
        }
    }

    // ── WP11.1 till naming ──

    private static async Task<(int storeId, Guid tenant, SqliteConnection conn)> SeededStore()
    {
        var tenant = Guid.NewGuid();
        var conn = OpenDb(tenant);
        using var ctx = Ctx(conn, tenant, "seed");
        var biz = new Business { Id = Guid.NewGuid(), Name = "Kapow", NameAbbr = "KAP", VatIN = "GB000" };
        ctx.Business.Add(biz);
        ctx.SaveChanges();
        var store = new Store
        {
            BusinessId = biz.Id, ContactNumber = "0", PostCode = "AB1 2CD",
            AdLine1 = "1 High St", AdLine2 = "", City = "Town", Country = "UK",
        };
        ctx.Stores.Add(store);
        ctx.SaveChanges();
        return (store.Id, tenant, conn);
    }

    [Fact]
    public async Task Till_names_are_tenant_unique_case_insensitive_at_create_and_rename()
    {
        var (storeId, tenant, conn) = await SeededStore();
        using (conn)
        {
            using (var ctx = Ctx(conn, tenant, "portal"))
                await new EnrolmentService(ctx, Opts).CreateTillAsync(tenant, storeId, "Front Desk", "portal");

            // Same name (any case) at create → 409.
            using (var ctx = Ctx(conn, tenant, "portal"))
            {
                var ex = await Assert.ThrowsAsync<EnrolmentException>(() =>
                    new EnrolmentService(ctx, Opts).CreateTillAsync(tenant, storeId, "front desk", "portal"));
                Assert.Equal(409, ex.StatusCode);
            }

            // A second, distinct till we can then try to rename into a clash.
            Guid secondId;
            using (var ctx = Ctx(conn, tenant, "portal"))
                secondId = (await new EnrolmentService(ctx, Opts).CreateTillAsync(tenant, storeId, "Back Office", "portal")).TillId;

            using (var ctx = Ctx(conn, tenant, "portal"))
            {
                var ex = await Assert.ThrowsAsync<EnrolmentException>(() =>
                    new EnrolmentService(ctx, Opts).RenameTillAsync(tenant, secondId, "FRONT DESK", "portal"));
                Assert.Equal(409, ex.StatusCode);
            }

            // A free name succeeds and is persisted.
            using (var ctx = Ctx(conn, tenant, "portal"))
                await new EnrolmentService(ctx, Opts).RenameTillAsync(tenant, secondId, "Kiosk 2", "portal");
            using (var ctx = Ctx(conn, tenant))
                Assert.Equal("Kiosk 2", (await ctx.TillDetails.FirstAsync(t => t.TillId == secondId)).Name);
        }
    }

    [Fact]
    public async Task Rename_upserts_details_for_a_till_that_predates_the_name_column()
    {
        var (storeId, tenant, conn) = await SeededStore();
        using (conn)
        {
            // A till with NO TillDetails row (as every till had before WP11.1).
            var tillId = Guid.NewGuid();
            using (var ctx = Ctx(conn, tenant, "seed"))
            {
                var till = new Till { Id = tillId, StoreId = storeId, LastOnline = DateTime.UtcNow };
                ctx.Till.Add(till);
                ctx.Entry(till).Property("TenantId").CurrentValue = tenant;
                ctx.SaveChanges();
            }

            using (var ctx = Ctx(conn, tenant, "portal"))
                await new EnrolmentService(ctx, Opts).RenameTillAsync(tenant, tillId, "Renamed", "portal");

            using (var ctx = Ctx(conn, tenant))
                Assert.Equal("Renamed", (await ctx.TillDetails.FirstAsync(t => t.TillId == tillId)).Name);
        }
    }
}
