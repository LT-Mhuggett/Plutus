using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// **Folding twice into ONE usage cell before saving — the bug that killed `RequestStatsFlusher`
/// on every cycle.**
///
/// ⚠⚠ `UsageMeter.Cell` used to go straight to the database with `FirstOrDefaultAsync`. A cell this
/// same `SaveChanges` had already CREATED has no row yet, so the query answered null, a second
/// instance was added with the same key, and EF threw *"another instance with the same key value
/// for {TenantId, BusinessDay, Metric} is already being tracked"*.
///
/// ⚠ IT WAS NOT A RARE RACE. `RequestStatsFlush` folds one snapshot per (tenant, **route group**)
/// into a single `api.requests` cell, so the second route group a tenant touched in any minute hit
/// it — every minute a tenant was awake. The flusher caught and continued, so the only trace was a
/// logged exception and a minute of request stats that silently never arrived.
///
/// ⚠ The fix is in the shared helper, not in the flusher, because the shape belongs to every caller
/// that folds several deltas into one cell before saving.
/// </summary>
public class UsageCellTrackingTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 8, 22);

    private static MySqlDbContext Unscoped(SqliteConnection conn)
        => new(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
               new FixedTenantContext(Guid.Empty)) { CurrentUser = "usage-cell-test" };

    private static SqliteConnection Open()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Unscoped(conn);
        ctx.Database.EnsureCreated();
        return conn;
    }

    /// <summary>The flusher's exact shape: several deltas into one cell, one SaveChanges.</summary>
    [Fact]
    public async Task Several_adds_to_one_new_cell_before_saving_sum_into_a_single_row()
    {
        using var conn = Open();

        using (var db = Unscoped(conn))
        {
            // ⚠ Three route groups in one minute, all folding into `api.requests`. Before the fix the
            // SECOND of these threw, and nothing in the minute was written.
            await UsageMeter.AddAsync(db, Tenant, Day, UsageMetrics.ApiRequests, 5);
            await UsageMeter.AddAsync(db, Tenant, Day, UsageMetrics.ApiRequests, 7);
            await UsageMeter.AddAsync(db, Tenant, Day, UsageMetrics.ApiRequests, 3);
            await db.SaveChangesAsync();
        }

        using (var db = Unscoped(conn))
        {
            var rows = await db.TenantUsageRollups.IgnoreQueryFilters().AsNoTracking()
                .Where(r => r.TenantId == Tenant && r.Metric == UsageMetrics.ApiRequests).ToListAsync();

            Assert.Single(rows);
            Assert.Equal(15, rows[0].Value);
        }
    }

    /// <summary>⚠ And the same again on a cell that ALREADY EXISTS — the find path must not double
    /// up either, or the second minute of any day would fork the row.</summary>
    [Fact]
    public async Task Several_adds_to_an_existing_cell_keep_folding_into_the_same_row()
    {
        using var conn = Open();

        using (var db = Unscoped(conn))
        {
            await UsageMeter.AddAsync(db, Tenant, Day, UsageMetrics.ApiRequests, 10);
            await db.SaveChangesAsync();
        }

        using (var db = Unscoped(conn))
        {
            await UsageMeter.AddAsync(db, Tenant, Day, UsageMetrics.ApiRequests, 4);
            await UsageMeter.AddAsync(db, Tenant, Day, UsageMetrics.ApiRequests, 6);
            await db.SaveChangesAsync();
        }

        using (var db = Unscoped(conn))
        {
            var rows = await db.TenantUsageRollups.IgnoreQueryFilters().AsNoTracking()
                .Where(r => r.TenantId == Tenant && r.Metric == UsageMetrics.ApiRequests).ToListAsync();

            Assert.Single(rows);
            Assert.Equal(20, rows[0].Value);
        }
    }

    /// <summary>⚠ Different metrics on the same day are DIFFERENT cells — the fix must not collapse
    /// them. This is why the sales consumer never tripped the bug: its two calls use two metrics.</summary>
    [Fact]
    public async Task Different_metrics_on_the_same_day_stay_separate_cells()
    {
        using var conn = Open();

        using (var db = Unscoped(conn))
        {
            await UsageMeter.AddAsync(db, Tenant, Day, UsageMetrics.ApiRequests, 5);
            await UsageMeter.AddAsync(db, Tenant, Day, UsageMetrics.SalesCount, 2);
            await db.SaveChangesAsync();
        }

        using (var db = Unscoped(conn))
        {
            var rows = await db.TenantUsageRollups.IgnoreQueryFilters().AsNoTracking()
                .Where(r => r.TenantId == Tenant && r.BusinessDay == Day).ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.Equal(5, rows.Single(r => r.Metric == UsageMetrics.ApiRequests).Value);
            Assert.Equal(2, rows.Single(r => r.Metric == UsageMetrics.SalesCount).Value);
        }
    }
}
