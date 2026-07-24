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
/// WP3.3 (DoD): rollups match direct aggregation of the sales TO THE PENNY; a rebuild from
/// scratch equals the incrementally-projected state; a late-arriving sale (old businessDay)
/// lands in the right day; replayed events don't double-count (drainer dedupe).
/// </summary>
public class RollupProjectionTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid BusinessId = Guid.NewGuid();
    private const int StoreId = 3;
    private static readonly Guid TillId = Guid.NewGuid();

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(Tenant)) { CurrentUser = "rollup-test" };

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

    /// <summary>Random valid sale: 1–3 lines, mixed 20%/0% VAT rates, on a random day.</summary>
    private static SaleV2 RandomSale(Random rng, long seq, DateOnly? day = null)
    {
        var saleId = Uuid7.New();
        var lines = new List<SaleLine>();
        int n = rng.Next(1, 4);
        for (var i = 0; i < n; i++)
        {
            long unit = rng.Next(1, 5000);
            int qty = rng.Next(1, 4);
            long gross = unit * qty;
            int rateBp = rng.Next(0, 2) == 0 ? 2000 : 0;
            long vat = rateBp == 0 ? 0 : gross - (long)Math.Round(gross / 1.2m);
            lines.Add(new SaleLine
            {
                Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId, LineNo = i + 1,
                ItemId = Guid.NewGuid(), Qty = qty, UnitPricePence = unit, DiscountPence = 0,
                LineGrossPence = gross, VatRateBp = rateBp, VatAmountPence = vat,
            });
        }
        long grossTotal = lines.Sum(l => l.LineGrossPence);
        long vatTotal = lines.Sum(l => l.VatAmountPence);
        var businessDay = day ?? new DateOnly(2026, 7, rng.Next(1, 26));
        return SaleV2.Create(saleId, Tenant, TillId, Guid.NewGuid(), seq, SaleChannel.WebPos,
            businessDay, DateTime.UtcNow, DateTime.UtcNow, grossTotal, vatTotal, lines,
            new[] { new SaleTender { Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId, TenderType = TenderType.Cash, AmountPence = grossTotal, ChangePence = 0 } });
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
        // Loop like the dispatcher's poll does — one call handles one batch (default 100).
        var drainer = new OutboxDrainer(new DefaultOutboxEventCodec(),
            new OutboxDispatcherOptions { RetryBackoffs = Array.Empty<TimeSpan>() });
        while (true)
        {
            using var db = Ctx(conn);
            var stats = await drainer.DrainConsumerAsync(db, new RollupProjectionConsumer(db), default);
            if (stats.Handled + stats.Skipped + stats.Parked == 0) break;
        }
    }

    [Fact]
    public async Task Incremental_rollups_match_direct_aggregation_to_the_penny()
    {
        using var conn = OpenSeeded();
        var rng = new Random(31337);
        var sales = Enumerable.Range(0, 200).Select(i => RandomSale(rng, i + 1)).ToList();
        using (var ctx = Ctx(conn))
        {
            foreach (var s in sales) Record(ctx, s);
            ctx.SaveChanges();
        }

        await DrainAsync(conn);

        using var check = Ctx(conn);
        var rollups = await check.SalesRollups.ToListAsync();

        // per till/day: exact match against direct aggregation of the source sales
        foreach (var g in sales.GroupBy(s => s.BusinessDay))
        {
            var row = Assert.Single(rollups, r => r.BusinessDay == g.Key && r.TillId == TillId);
            Assert.Equal(g.Sum(s => s.GrossPence), row.GrossPence);
            Assert.Equal(g.Sum(s => s.VatPence), row.VatPence);
            Assert.Equal(g.Count(), row.TxnCount);
            Assert.Equal(BusinessId, row.CompanyId);
            Assert.Equal(StoreId, row.StoreId);
        }
        Assert.Equal(sales.Sum(s => s.GrossPence), rollups.Sum(r => r.GrossPence));

        // VAT per rate per day: exact
        var vatRollups = await check.VatRollups.ToListAsync();
        foreach (var g in sales.SelectMany(s => s.Lines.Select(l => (s.BusinessDay, l)))
                     .GroupBy(x => (x.BusinessDay, x.l.VatRateBp)))
        {
            var row = Assert.Single(vatRollups, r => r.BusinessDay == g.Key.BusinessDay && r.VatRateBp == g.Key.VatRateBp);
            Assert.Equal(g.Sum(x => x.l.LineGrossPence), row.GrossPence);
            Assert.Equal(g.Sum(x => x.l.VatAmountPence), row.VatPence);
            Assert.Equal(g.Sum(x => x.l.LineGrossPence - x.l.VatAmountPence), row.NetPence);
        }
    }

    [Fact]
    public async Task Rebuild_from_scratch_equals_the_incremental_result()
    {
        using var conn = OpenSeeded();
        var rng = new Random(77);
        using (var ctx = Ctx(conn))
        {
            foreach (var s in Enumerable.Range(0, 120).Select(i => RandomSale(rng, i + 1))) Record(ctx, s);
            ctx.SaveChanges();
        }
        await DrainAsync(conn);

        static (List<(Guid, string, long, long, int)> S, List<(int, string, int, long, long, long)> V) Snapshot(MySqlDbContext db) => (
            db.SalesRollups.AsNoTracking().AsEnumerable()
                .Select(r => (r.TillId, r.BusinessDay.ToString("O"), r.GrossPence, r.VatPence, r.TxnCount))
                .OrderBy(x => x.Item2).ToList(),
            db.VatRollups.AsNoTracking().AsEnumerable()
                .Select(r => (r.StoreId, r.BusinessDay.ToString("O"), r.VatRateBp, r.GrossPence, r.NetPence, r.VatPence))
                .OrderBy(x => x.Item2).ThenBy(x => x.Item3).ToList());

        (List<(Guid, string, long, long, int)>, List<(int, string, int, long, long, long)>) before, after;
        using (var db = Ctx(conn)) before = Snapshot(db);
        using (var db = Ctx(conn)) await RollupRebuilder.RebuildAsync(db, Tenant);
        using (var db = Ctx(conn)) after = Snapshot(db);

        Assert.Equal(before.Item1, after.Item1);
        Assert.Equal(before.Item2, after.Item2);

        // and the rebuild advanced the consumer offset — a subsequent drain reapplies nothing
        await DrainAsync(conn);
        using (var db = Ctx(conn))
        {
            var final = Snapshot(db);
            Assert.Equal(after.Item1, final.Item1);
        }
    }

    [Fact]
    public async Task Late_arriving_sale_lands_in_its_business_day_not_today()
    {
        using var conn = OpenSeeded();
        var oldDay = new DateOnly(2026, 6, 1); // "yesterday's" trade arriving late
        using (var ctx = Ctx(conn))
        {
            Record(ctx, RandomSale(new Random(1), 1, oldDay));
            ctx.SaveChanges();
        }
        await DrainAsync(conn);

        using var check = Ctx(conn);
        var row = Assert.Single(await check.SalesRollups.ToListAsync());
        Assert.Equal(oldDay, row.BusinessDay);
    }

    [Fact]
    public async Task Replayed_events_do_not_double_count()
    {
        using var conn = OpenSeeded();
        using (var ctx = Ctx(conn))
        {
            Record(ctx, RandomSale(new Random(2), 1, new DateOnly(2026, 7, 20)));
            ctx.SaveChanges();
        }

        await DrainAsync(conn);
        // wipe the offset (a crash-before-offset-advance would re-deliver); ProcessedEvents dedupes
        using (var ctx = Ctx(conn))
        {
            var off = await ctx.ConsumerOffsets.FirstAsync(o => o.ConsumerName == RollupProjectionConsumer.ConsumerName);
            off.LastOutboxId = 0;
            await ctx.SaveChangesAsync();
        }
        await DrainAsync(conn);

        using var check = Ctx(conn);
        Assert.Equal(1, (await check.SalesRollups.SingleAsync()).TxnCount);
    }
}
