using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.Infrastructure.Outbox;
using Plutus.Reporting;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP3.4 (DoD): closing a year then ingesting a late sale leaves the closed year's figures
/// byte-identical (the late sale posts to the next open day, flagged); a rebuild AFTER the
/// close reproduces the same locked figures; export totals match the rollups.
/// </summary>
public class FinancialPeriodTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid BusinessId = Guid.NewGuid();
    private const int StoreId = 1;
    private static readonly Guid TillId = Guid.NewGuid();

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(Tenant)) { CurrentUser = "period-test" };

    private static SqliteConnection OpenSeeded()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Ctx(conn);
        ctx.Database.EnsureCreated();
        ctx.Business.Add(new Business { Id = BusinessId, Name = "Testco", NameAbbr = "TST", VatIN = "GB0" });
        ctx.Stores.Add(new Store
        {
            Id = StoreId, BusinessId = BusinessId, ContactNumber = "-",
            AdLine1 = "-", AdLine2 = "", City = "-", PostCode = "-", Country = "-",
        });
        ctx.Till.Add(new Till { Id = TillId, StoreId = StoreId, LastOnline = DateTime.UtcNow });
        ctx.SaveChanges();
        return conn;
    }

    private static SaleV2 Sale(long seq, DateOnly day, long grossPence)
    {
        var saleId = Uuid7.New();
        long vat = grossPence - (long)Math.Round(grossPence / 1.2m);
        var line = new SaleLine
        {
            Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId, LineNo = 1,
            ItemId = Guid.NewGuid(), Qty = 1, UnitPricePence = grossPence, DiscountPence = 0,
            LineGrossPence = grossPence, VatRateBp = 2000, VatAmountPence = vat,
        };
        return SaleV2.Create(saleId, Tenant, TillId, Guid.NewGuid(), seq, SaleChannel.WebPos,
            day, DateTime.UtcNow, DateTime.UtcNow, grossPence, vat, new[] { line },
            new[] { new SaleTender { Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId, TenderType = TenderType.Cash, AmountPence = grossPence, ChangePence = 0 } });
    }

    private static void Record(MySqlDbContext ctx, SaleV2 sale)
    {
        ctx.SalesV2.Add(sale);
        var evt = new SaleRecorded(Uuid7.New(), Tenant, sale.OccurredAtUtc, sale.Id, sale.DeviceId, sale.DeviceSeq, sale.BusinessDay);
        ctx.OutboxEvents.Add(new OutboxEvent
        {
            EventId = evt.EventId, TenantId = Tenant, EventType = nameof(SaleRecorded),
            PayloadJson = JsonSerializer.Serialize(evt), CreatedAtUtc = DateTime.UtcNow,
        });
    }

    private static async Task DrainAsync(SqliteConnection conn)
    {
        var drainer = new OutboxDrainer(new DefaultOutboxEventCodec(),
            new OutboxDispatcherOptions { RetryBackoffs = Array.Empty<TimeSpan>() });
        while (true)
        {
            using var db = Ctx(conn);
            var stats = await drainer.DrainConsumerAsync(db, new RollupProjectionConsumer(db), default);
            if (stats.Handled + stats.Skipped + stats.Parked == 0) break;
        }
    }

    private static void ClosePeriod(SqliteConnection conn, DateOnly start, DateOnly end)
    {
        using var ctx = Ctx(conn);
        ctx.FinancialPeriods.Add(new FinancialPeriod
        {
            Id = Uuid7.New(), TenantId = Tenant, CompanyId = BusinessId, Name = "FY",
            StartDay = start, EndDay = end, Status = PeriodStatus.Closed,
            ClosedAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow,
        });
        ctx.SaveChanges();
    }

    [Fact]
    public async Task Late_sale_after_close_leaves_the_closed_year_byte_identical_and_is_flagged()
    {
        using var conn = OpenSeeded();
        var yEnd = new DateOnly(2025, 12, 31);

        // trade inside the year, project, close the year
        using (var ctx = Ctx(conn))
        {
            Record(ctx, Sale(1, new DateOnly(2025, 6, 1), 1200));
            Record(ctx, Sale(2, new DateOnly(2025, 11, 5), 2400));
            ctx.SaveChanges();
        }
        await DrainAsync(conn);
        ClosePeriod(conn, new DateOnly(2025, 1, 1), yEnd);

        List<(string, long, long, int)> ClosedYear(MySqlDbContext db) =>
            db.SalesRollups.AsNoTracking().AsEnumerable()
                .Where(r => r.BusinessDay <= yEnd)
                .Select(r => (r.BusinessDay.ToString("O"), r.GrossPence, r.VatPence, r.TxnCount))
                .OrderBy(x => x.Item1).ToList();

        List<(string, long, long, int)> before;
        using (var db = Ctx(conn)) before = ClosedYear(db);

        // a late sale dated INSIDE the closed year arrives
        using (var ctx = Ctx(conn))
        {
            Record(ctx, Sale(3, new DateOnly(2025, 12, 15), 999));
            ctx.SaveChanges();
        }
        await DrainAsync(conn);

        using (var db = Ctx(conn))
        {
            // the closed year is byte-identical
            Assert.Equal(before, ClosedYear(db));

            // the late sale posted to the first open day (Jan 1) and was flagged
            var jan1 = await db.SalesRollups.SingleAsync(r => r.BusinessDay == new DateOnly(2026, 1, 1));
            Assert.Equal(999, jan1.GrossPence);
            var flag = Assert.Single(await db.AuditLogs.Where(a => a.Action == "period.late-post").ToListAsync());
            Assert.Contains("2025-12-15", flag.DetailJson);
            Assert.Contains("2026-01-01", flag.DetailJson);
        }
    }

    [Fact]
    public async Task Rebuild_after_close_reproduces_the_locked_figures()
    {
        using var conn = OpenSeeded();
        using (var ctx = Ctx(conn))
        {
            Record(ctx, Sale(1, new DateOnly(2025, 3, 3), 5000));
            ctx.SaveChanges();
        }
        await DrainAsync(conn);
        ClosePeriod(conn, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));

        using (var ctx = Ctx(conn))
        {
            Record(ctx, Sale(2, new DateOnly(2025, 7, 7), 700)); // late, into the closed year
            ctx.SaveChanges();
        }
        await DrainAsync(conn);

        List<(string, long, int)> Snapshot(MySqlDbContext db) =>
            db.SalesRollups.AsNoTracking().AsEnumerable()
                .Select(r => (r.BusinessDay.ToString("O"), r.GrossPence, r.TxnCount))
                .OrderBy(x => x.Item1).ToList();

        List<(string, long, int)> incremental;
        using (var db = Ctx(conn)) incremental = Snapshot(db);
        Assert.Equal(2, incremental.Count); // 2025-03-03 (locked) + 2026-01-01 (redirected)

        using (var db = Ctx(conn)) await RollupRebuilder.RebuildAsync(db, Tenant);
        using (var db = Ctx(conn)) Assert.Equal(incremental, Snapshot(db));
    }

    [Fact]
    public async Task Adjacent_closed_periods_chain_to_the_first_open_day()
    {
        using var conn = OpenSeeded();
        ClosePeriod(conn, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        ClosePeriod(conn, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

        using (var ctx = Ctx(conn))
        {
            Record(ctx, Sale(1, new DateOnly(2025, 6, 6), 100)); // deep in the first closed period
            ctx.SaveChanges();
        }
        await DrainAsync(conn);

        using var check = Ctx(conn);
        var row = Assert.Single(await check.SalesRollups.ToListAsync());
        Assert.Equal(new DateOnly(2026, 4, 1), row.BusinessDay); // past BOTH closed periods
    }
}
