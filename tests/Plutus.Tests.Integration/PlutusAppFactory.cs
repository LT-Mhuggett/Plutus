using System;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Plutus.DBService;
using Plutus.Entities;
using Plutus.SharedKernel;

namespace Plutus.Tests.Integration;

/// <summary>
/// Boots the real host (all modules, real auth handler + scope policies, ingest, outbox) with
/// the RepositoryContext swapped to a MySqlDbContext on a single shared in-memory SQLite
/// connection — so the tenancy/sales-v2 tables exist and MigrateDatabase creates them via
/// EnsureCreated (PLUTUS_DB_ENSURE_CREATED). Tokens are minted with the same HMAC secret the
/// PlutusTokenAuthHandler validates.
/// </summary>
public class PlutusAppFactory : WebApplicationFactory<Program>
{
    public const string Secret = "integration-test-secret";
    public const string JobsSecret = "integration-jobs-secret";
    private readonly SqliteConnection _conn;

    public PlutusAppFactory()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open(); // keep open for the factory's lifetime so the in-memory schema persists
        Environment.SetEnvironmentVariable("DISABLE_AUTH_DEV_ONLY", "true");
        Environment.SetEnvironmentVariable("TEST_TOKEN_SECRET", Secret);
        Environment.SetEnvironmentVariable("PLUTUS_DB_ENSURE_CREATED", "true");
        Environment.SetEnvironmentVariable("JOBS_REPORT_SECRET", JobsSecret); // WP13.3 HMAC job report
        Environment.SetEnvironmentVariable("RATE_LIMIT_ENROL_PER_MIN", "100000"); // don't throttle the test IP
        Environment.SetEnvironmentVariable("RATE_LIMIT_DEFAULT_RPS", "30"); // WP13.5: high enough for normal tests, floodable in one
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            // Drop whichever DbContext the host registered (SqliteDbContext in DEBUG,
            // MySqlDbContext in Release) and rebind RepositoryContext to a MySqlDbContext on
            // our shared SQLite connection.
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<DbContextOptions<RepositoryContext>>();
            services.RemoveAll<DbContextOptions<MySqlDbContext>>();
            services.RemoveAll<DbContextOptions<SqliteDbContext>>();
            services.RemoveAll<RepositoryContext>();
            services.RemoveAll<MySqlDbContext>();
            services.RemoveAll<SqliteDbContext>();

            services.AddDbContext<RepositoryContext, MySqlDbContext>(o => o.UseSqlite(_conn));

            // Stop the background hosted services — their polling opens its own transactions on
            // our single shared SQLite connection and races the test requests AND the startup
            // EnsureCreated (SQLite is single-writer / single-connection, not thread-safe). Their
            // logic is covered by unit tests (OutboxDispatcherTests, UsageMeteringTests). On real
            // MySQL each context gets its own pooled connection, so this race can't happen there.
            foreach (var d in services
                         .Where(d => d.ImplementationType?.FullName is
                             "Plutus.Infrastructure.Outbox.OutboxDispatcher" or
                             "Plutus.Tenancy.RetentionSweeper" or
                             "Plutus.Infrastructure.Health.RequestStatsFlusher" or
                             // ⚠ ADDED 2026-08-11, and it was already breaking things. The
                             // reconciler enumerates `Tenants` on boot to grant missing built-in
                             // permissions — correct in production, and on this host it races the
                             // startup `EnsureCreated` on the one shared connection. The symptom is
                             // `SQLite Error 1: 'no such table: Tenants'` surfacing as a transient
                             // MySQL retry suggestion, which points nowhere near the real cause.
                             // ⚠ It was intermittent: it depended on which class booted the host
                             // and how fast its first request arrived, so the suite stayed green
                             // until `StockAdjustmentReportE2eTests` shifted the timing and took
                             // out all eight of its tests at once.
                             "Plutus.Identity.RolePermissionReconciler")
                         .ToList())
                services.Remove(d);
        });
    }

    // ---- token helpers (mirror what the login endpoint / device flow would issue) ----
    public static string OperatorToken(string scope, Guid? tid = null) =>
        OperatorTokenFor(Guid.NewGuid(), scope, tid);

    /// <summary>WP3.2: a token for a KNOWN employee id, so RBAC (perm:*) gates — which
    /// resolve against RbacRoleAssignments, not scope claims — can be exercised.</summary>
    public static string OperatorTokenFor(Guid employeeId, string scope, Guid? tid = null) => CompactToken.Issue(
        JsonSerializer.Serialize(new
        {
            EmployeeId = employeeId,
            Name = "integration",
            Scope = scope,
            Tid = tid,
            Exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        }), Secret);

    /// <summary>
    /// A DEVICE token — what an enrolled till actually holds: scope "device" plus the `did` claim
    /// naming which device it is.
    ///
    /// ⚠ The `did` is the point. Endpoints that a till may call on ITSELF (un-enrol request, its own
    /// status) read it to check the token is talking about its own machine; a scope-only device
    /// token would let any enrolled till act on any other.
    /// </summary>
    /// ⚠ LOWERCASE claim names, unlike the operator token above. `PlutusTokenAuthHandler` reads a
    /// device token as `did` / `tid` / `scope` and `JsonElement.TryGetProperty` is case-SENSITIVE,
    /// so `Did` parses as no device at all — the token authenticates but identifies nothing, and
    /// every "is this your own device?" check silently passes.
    public static string DeviceToken(Guid deviceId, Guid? tid = null) => CompactToken.Issue(
        JsonSerializer.Serialize(new
        {
            did = deviceId.ToString(),
            tid = tid?.ToString(),
            scope = PlutusPolicies.Device,
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        }), Secret);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // A background query (e.g. the rate limiter's fire-and-forget entitlement refresh) can
        // still hold the single shared in-memory connection at teardown, making Close() NRE. That's
        // a test-harness artifact of the one-connection model — production uses pooled MySQL
        // connections. Swallow it so a clean test run isn't marked failed by disposal.
        if (disposing) { try { _conn.Dispose(); } catch { /* teardown race on the shared connection */ } }
    }
}
