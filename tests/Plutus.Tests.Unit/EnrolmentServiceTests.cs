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
        }
    }
}
