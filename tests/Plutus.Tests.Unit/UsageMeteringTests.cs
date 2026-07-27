using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

/// <summary>
/// WP13.1 usage metering DoD: a day of simulated trading across TWO tenants produces per-tenant
/// sales.count / sales.grossPence cells matching a direct group-by, and a rebuild from SalesV2
/// reproduces the incremental fold exactly (rebuild == incremental). The consumer runs under an
/// unscoped (Guid.Empty) context — as the live outbox path effectively does — so both tenants'
/// rows are written from one drain.
/// </summary>
public class UsageMeteringTests
{
    private static readonly Guid T1 = Guid.NewGuid();
    private static readonly Guid T2 = Guid.NewGuid();
    private static readonly DateOnly DayA = new(2026, 7, 1);
    private static readonly DateOnly DayB = new(2026, 7, 2);

    private static MySqlDbContext Unscoped(SqliteConnection conn)
        => new(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
               new FixedTenantContext(Guid.Empty)) { CurrentUser = "usage-test" };

    private static SqliteConnection Open()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Unscoped(conn);
        ctx.Database.EnsureCreated();
        return conn;
    }

    private static SaleV2 Sale(Guid tenant, DateOnly day, long gross)
    {
        var id = Uuid7.New();
        var line = new SaleLine
        {
            Id = Uuid7.New(), TenantId = tenant, SaleId = id, LineNo = 1,
            ItemId = Guid.NewGuid(), ItemIdOne = "X", Qty = 1,
            UnitPricePence = gross, LineGrossPence = gross, DiscountPence = 0, VatRateBp = 0, VatAmountPence = 0,
        };
        var tender = new SaleTender
        {
            Id = Uuid7.New(), TenantId = tenant, SaleId = id, TenderType = TenderType.Cash, AmountPence = gross, ChangePence = 0,
        };
        return SaleV2.Create(id, tenant, Uuid7.New(), Uuid7.New(), 1, SaleChannel.Till,
            day, DateTime.UtcNow, DateTime.UtcNow, gross, 0, new[] { line }, new[] { tender });
    }

    /// <summary>Seed the fixture: T1 does 1000+500 on day A and 2000 on day B; T2 does 300 on day A.</summary>
    private static List<SaleV2> Seed(SqliteConnection conn)
    {
        var sales = new List<SaleV2>
        {
            Sale(T1, DayA, 1000), Sale(T1, DayA, 500), Sale(T1, DayB, 2000),
            Sale(T2, DayA, 300),
        };
        using var db = Unscoped(conn);
        db.SalesV2.AddRange(sales);
        db.SaveChanges();
        return sales;
    }

    private static long Cell(MySqlDbContext db, Guid tenant, DateOnly day, string metric) =>
        db.TenantUsageRollups.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenant && x.BusinessDay == day && x.Metric == metric)
            .Select(x => x.Value).FirstOrDefault();

    [Fact]
    public async Task Event_fed_fold_produces_per_tenant_daily_sales_metrics()
    {
        using var conn = Open();
        var sales = Seed(conn);

        // Drive the consumer exactly like the drainer: HandleAsync then SaveChanges per event.
        using (var db = Unscoped(conn))
        {
            var consumer = new UsageMeteringConsumer(db);
            foreach (var s in sales)
            {
                await consumer.HandleAsync(
                    new SaleRecorded(Uuid7.New(), s.TenantId, DateTime.UtcNow, s.Id, Guid.NewGuid(), 1, s.BusinessDay),
                    CancellationToken.None);
                await db.SaveChangesAsync();
            }
        }

        using var check = Unscoped(conn);
        Assert.Equal(2, Cell(check, T1, DayA, UsageMetrics.SalesCount));
        Assert.Equal(1500, Cell(check, T1, DayA, UsageMetrics.SalesGrossPence));
        Assert.Equal(1, Cell(check, T1, DayB, UsageMetrics.SalesCount));
        Assert.Equal(2000, Cell(check, T1, DayB, UsageMetrics.SalesGrossPence));
        Assert.Equal(1, Cell(check, T2, DayA, UsageMetrics.SalesCount));
        Assert.Equal(300, Cell(check, T2, DayA, UsageMetrics.SalesGrossPence));
        // isolation: T2 never picked up T1's trade
        Assert.Equal(0, Cell(check, T2, DayB, UsageMetrics.SalesCount));
    }

    [Fact]
    public async Task Rebuild_equals_incremental()
    {
        using var conn = Open();
        var sales = Seed(conn);

        // incremental fold
        using (var db = Unscoped(conn))
        {
            var consumer = new UsageMeteringConsumer(db);
            foreach (var s in sales)
            {
                await consumer.HandleAsync(
                    new SaleRecorded(Uuid7.New(), s.TenantId, DateTime.UtcNow, s.Id, Guid.NewGuid(), 1, s.BusinessDay),
                    CancellationToken.None);
                await db.SaveChangesAsync();
            }
        }
        var incremental = Snapshot(conn);

        // rebuild both tenants from SalesV2
        using (var db = Unscoped(conn)) { await UsageRebuilder.RebuildAsync(db, T1); }
        using (var db = Unscoped(conn)) { await UsageRebuilder.RebuildAsync(db, T2); }
        var rebuilt = Snapshot(conn);

        Assert.Equal(incremental, rebuilt);
        // and the values are the expected ones (not both empty)
        Assert.Equal(1500, rebuilt[(T1, DayA, UsageMetrics.SalesGrossPence)]);
    }

    private static Dictionary<(Guid, DateOnly, string), long> Snapshot(SqliteConnection conn)
    {
        using var db = Unscoped(conn);
        return db.TenantUsageRollups.IgnoreQueryFilters().AsNoTracking()
            .ToList()
            .ToDictionary(x => (x.TenantId, x.BusinessDay, x.Metric), x => x.Value);
    }

    private static void SeedTenantSpine(SqliteConnection conn, Guid tenant, int storeIdStart,
        int storeCount, int tillCount, int peopleCount, long[] sales)
    {
        var bizId = Guid.NewGuid();
        using var db = new MySqlDbContext(
            new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
            new FixedTenantContext(tenant)) { CurrentUser = "seed" }; // per-tenant ctx stamps the shadow TenantId
        db.Business.Add(new Business { Id = bizId, Name = "B", NameAbbr = "B", VatIN = "GB0" });
        for (int i = 0; i < storeCount; i++)
            db.Stores.Add(new Store { Id = storeIdStart + i, BusinessId = bizId, ContactNumber = "-", AdLine1 = "-", AdLine2 = "", City = "-", PostCode = "-", Country = "-" });
        for (int i = 0; i < tillCount; i++)
            db.Till.Add(new Till { Id = Guid.NewGuid(), StoreId = storeIdStart, LastOnline = DateTime.UtcNow });
        for (int i = 0; i < peopleCount; i++)
            db.People.Add(new Person { Id = Guid.NewGuid(), FName = "P", LName = "Q", Email = $"{Guid.NewGuid():N}@t.local", Mobile = "0", AdLine1 = "-", AdLine2 = "", City = "-", PostCode = "-", Country = "-" });
        foreach (var g in sales) db.SalesV2.Add(Sale(tenant, DayA, g));
        db.SaveChanges();
    }

    [Fact]
    public async Task Nightly_sweep_counts_stores_tills_users_and_sales_per_tenant()
    {
        using var conn = Open();
        SeedTenantSpine(conn, T1, storeIdStart: 1, storeCount: 2, tillCount: 1, peopleCount: 3, sales: new long[] { 1000, 500 });
        SeedTenantSpine(conn, T2, storeIdStart: 10, storeCount: 1, tillCount: 2, peopleCount: 1, sales: new long[] { 300 });
        using (var db = Unscoped(conn))
        {
            db.Tenants.Add(new Tenant { Id = T1, Name = "T1", Status = 1, Plan = "std", Entitlements = "[]", ConnectionRef = "", CreatedAtUtc = DateTime.UtcNow });
            db.Tenants.Add(new Tenant { Id = T2, Name = "T2", Status = 1, Plan = "std", Entitlements = "[]", ConnectionRef = "", CreatedAtUtc = DateTime.UtcNow });
            db.SaveChanges();
        }

        using (var db = Unscoped(conn)) { await UsageSweep.RunAsync(db, DayA); }

        using var check = Unscoped(conn);
        Assert.Equal(2, Cell(check, T1, DayA, UsageMetrics.StoresActive));
        Assert.Equal(1, Cell(check, T1, DayA, UsageMetrics.TillsActive));
        Assert.Equal(3, Cell(check, T1, DayA, UsageMetrics.UsersActive));
        Assert.Equal(2, Cell(check, T1, DayA, UsageMetrics.StorageRowsSalesV2));
        Assert.Equal(1, Cell(check, T2, DayA, UsageMetrics.StoresActive));
        Assert.Equal(2, Cell(check, T2, DayA, UsageMetrics.TillsActive));
        Assert.Equal(1, Cell(check, T2, DayA, UsageMetrics.UsersActive));
        Assert.Equal(1, Cell(check, T2, DayA, UsageMetrics.StorageRowsSalesV2));

        // idempotent: a second sweep sets the same values (not doubled)
        using (var db = Unscoped(conn)) { await UsageSweep.RunAsync(db, DayA); }
        using var check2 = Unscoped(conn);
        Assert.Equal(2, Cell(check2, T1, DayA, UsageMetrics.StoresActive));
        Assert.Equal(2, Cell(check2, T1, DayA, UsageMetrics.StorageRowsSalesV2));
    }
}
