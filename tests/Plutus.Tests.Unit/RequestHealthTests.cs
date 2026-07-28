using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.Infrastructure.Health;
using Plutus.Tenancy;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP13.2 request health: the in-memory accumulator isolates tenants (a burst against A never
/// shows against B), latency percentiles come off the fixed histogram, the per-minute flush
/// persists rows + folds api.requests, and the 35-day retention purge keeps the window.
/// </summary>
public class RequestHealthTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    private static MySqlDbContext Unscoped(SqliteConnection conn)
        => new(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
               new FixedTenantContext(Guid.Empty)) { CurrentUser = "req-health-test" };

    private static SqliteConnection Open()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Unscoped(conn);
        ctx.Database.EnsureCreated();
        return conn;
    }

    [Theory]
    [InlineData("/api/v1/sales", "sales")]
    [InlineData("/api/v1/customers/11111111-1111-1111-1111-111111111111", "customers")]
    [InlineData("/api/v1/platform/usage/summary", "platform/usage")]
    [InlineData("/api/Item/Index", "item/index")]
    [InlineData("/", "root")]
    public void Route_groups_are_coarse_and_bounded(string path, string expected) =>
        Assert.Equal(expected, RouteGroups.Of(path));

    [Fact]
    public void Accumulator_isolates_tenants_and_counts_errors()
    {
        var acc = new RequestStatsAccumulator();
        // tenant A: a burst of 500s on "sales"; tenant B: healthy on "reports"
        for (int i = 0; i < 20; i++) acc.Record(A, "sales", 30, 500);
        for (int i = 0; i < 5; i++) acc.Record(A, "sales", 12, 200);
        for (int i = 0; i < 8; i++) acc.Record(B, "reports", 8, 200);

        var snaps = acc.SnapshotAndReset();
        var a = snaps.Single(s => s.TenantId == A && s.RouteGroup == "sales");
        var b = snaps.Single(s => s.TenantId == B && s.RouteGroup == "reports");

        Assert.Equal(25, a.Count);
        Assert.Equal(20, a.Err5xx);
        Assert.Equal(0, b.Err5xx);          // B never saw A's burst
        Assert.Equal(8, b.Count);
        // p95 of A (mostly 30ms) lands in a bucket ≥ 25ms; B (8ms) in a low bucket
        Assert.True(a.P95Ms >= 25);
        Assert.True(b.P95Ms <= 10);

        // reset really reset — a second snapshot is empty
        Assert.Empty(acc.SnapshotAndReset());
    }

    [Fact]
    public async Task Flush_persists_rows_and_folds_api_requests()
    {
        using var conn = Open();
        var acc = new RequestStatsAccumulator();
        for (int i = 0; i < 10; i++) acc.Record(A, "sales", 20, 200);
        for (int i = 0; i < 3; i++) acc.Record(A, "sales", 20, 404);
        for (int i = 0; i < 4; i++) acc.Record(B, "reports", 20, 200);

        var minute = new DateTime(2026, 7, 28, 9, 0, 0, DateTimeKind.Utc);
        using (var db = Unscoped(conn))
            await RequestStatsFlush.WriteAsync(db, acc.SnapshotAndReset(), minute);

        using var check = Unscoped(conn);
        var aRow = check.TenantRequestStats.IgnoreQueryFilters().Single(x => x.TenantId == A);
        Assert.Equal(13, aRow.Count);
        Assert.Equal(3, aRow.Err4xx);
        Assert.Equal(minute, aRow.MinuteUtc);

        // api.requests folded into the usage rollup for that day, per tenant
        var day = DateOnly.FromDateTime(minute);
        long ApiReq(Guid t) => check.TenantUsageRollups.IgnoreQueryFilters()
            .Where(x => x.TenantId == t && x.BusinessDay == day && x.Metric == UsageMetrics.ApiRequests)
            .Select(x => x.Value).FirstOrDefault();
        Assert.Equal(13, ApiReq(A));
        Assert.Equal(4, ApiReq(B));
    }

    [Fact]
    public async Task Retention_purges_only_rows_past_the_window()
    {
        using var conn = Open();
        var now = new DateTime(2026, 7, 28, 12, 0, 0, DateTimeKind.Utc);
        using (var db = Unscoped(conn))
        {
            db.TenantRequestStats.Add(new TenantRequestStats { TenantId = A, MinuteUtc = now.AddDays(-40), RouteGroup = "sales", Count = 1 });
            db.TenantRequestStats.Add(new TenantRequestStats { TenantId = A, MinuteUtc = now.AddDays(-10), RouteGroup = "sales", Count = 1 });
            db.SaveChanges();
        }

        int purged;
        using (var db = Unscoped(conn))
            purged = await RequestStatsRetention.PurgeAsync(db, now.AddDays(-RequestStatsRetention.RetentionDays));

        Assert.Equal(1, purged);
        using var check = Unscoped(conn);
        Assert.Single(check.TenantRequestStats.IgnoreQueryFilters().ToList()); // the -10d row survives
    }

    [Fact]
    public void Record_overhead_is_well_under_a_millisecond()
    {
        var acc = new RequestStatsAccumulator();
        acc.Record(A, "warmup", 1, 200); // JIT
        const int n = 200_000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++) acc.Record(A, "sales", i & 1023, 200);
        sw.Stop();
        var perCallMicros = sw.Elapsed.TotalMilliseconds * 1000.0 / n;
        Assert.True(perCallMicros < 50, $"Record averaged {perCallMicros:F2}µs/call — expected well under the 1ms (1000µs) budget.");
    }
}
