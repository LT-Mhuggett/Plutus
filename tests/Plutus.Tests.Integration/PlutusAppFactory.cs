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
public sealed class PlutusAppFactory : WebApplicationFactory<Program>
{
    public const string Secret = "integration-test-secret";
    private readonly SqliteConnection _conn;

    public PlutusAppFactory()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open(); // keep open for the factory's lifetime so the in-memory schema persists
        Environment.SetEnvironmentVariable("DISABLE_AUTH_DEV_ONLY", "true");
        Environment.SetEnvironmentVariable("TEST_TOKEN_SECRET", Secret);
        Environment.SetEnvironmentVariable("PLUTUS_DB_ENSURE_CREATED", "true");
        Environment.SetEnvironmentVariable("RATE_LIMIT_ENROL_PER_MIN", "100000"); // don't throttle the test IP
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

            // Stop the OutboxDispatcher hosted service — its background polling opens its own
            // transactions on our single shared SQLite connection and races the test requests
            // (SQLite is single-writer). Dispatch itself is covered by OutboxDispatcherTests.
            foreach (var d in services
                         .Where(d => d.ImplementationType?.FullName == "Plutus.Infrastructure.Outbox.OutboxDispatcher")
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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _conn.Dispose();
    }
}
