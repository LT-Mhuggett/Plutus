using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Catalogue;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.Infrastructure.Outbox;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP5.1 (DoD): level == ledger sum ALWAYS (property test over random typed movements);
/// the sale consumer is idempotent under replay; returns restock; rebuild reproduces the
/// levels from the ledger; the opening-balance seed is idempotent.
/// </summary>
public class StockLedgerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid BusinessId = Guid.NewGuid();
    private const int StoreId = 1;
    private static readonly Guid TillId = Guid.NewGuid();

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(Tenant)) { CurrentUser = "stock-test" };

    private static SqliteConnection OpenSeeded()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Ctx(conn);
        ctx.Database.EnsureCreated();
        ctx.Business.Add(new Business { Id = BusinessId, Name = "Testco", NameAbbr = "TST", VatIN = "GB0" });
        ctx.Till.Add(new Till { Id = TillId, StoreId = StoreId, LastOnline = DateTime.UtcNow });
        ctx.SaveChanges();
        return conn;
    }

    private static void RecordSale(MySqlDbContext ctx, Guid saleId, string itemIdOne, int qty)
    {
        long unit = 100;
        long gross = unit * qty;
        var line = new SaleLine
        {
            Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId, LineNo = 1,
            ItemId = DeterministicGuid.ForItem(BusinessId, itemIdOne), Qty = qty,
            UnitPricePence = unit, DiscountPence = 0, LineGrossPence = gross,
            VatRateBp = 0, VatAmountPence = 0,
            DiscountsJson = JsonSerializer.Serialize(new { itemIdOne }),
        };
        var sale = SaleV2.Create(saleId, Tenant, TillId, Guid.NewGuid(), 1, SaleChannel.WebPos,
            new DateOnly(2026, 7, 25), DateTime.UtcNow, DateTime.UtcNow, gross, 0,
            new[] { line },
            new[] { new SaleTender { Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId, TenderType = TenderType.Cash, AmountPence = gross, ChangePence = 0 } });
        ctx.SalesV2.Add(sale);
        var evt = new SaleRecorded(Uuid7.New(), Tenant, sale.OccurredAtUtc, saleId, sale.DeviceId, 1, sale.BusinessDay);
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
            var stats = await drainer.DrainConsumerAsync(db, new StockProjectionConsumer(db), default);
            if (stats.Handled + stats.Skipped + stats.Parked == 0) break;
        }
    }

    [Fact]
    public async Task Level_always_equals_ledger_sum_under_random_typed_movements()
    {
        using var conn = OpenSeeded();
        var rng = new Random(2026);
        var items = new[] { "A-1", "B-2", "C-3" };

        using (var db = Ctx(conn))
        {
            var service = new StockLedgerService(db);
            var location = await service.EnsureStoreLocationAsync(Tenant, StoreId);
            foreach (var i in Enumerable.Range(0, 300))
            {
                var item = items[rng.Next(items.Length)];
                var type = (StockMovementType)rng.Next(0, 7);
                var qty = type switch
                {
                    StockMovementType.Sale or StockMovementType.WriteOff or StockMovementType.TransferOut
                        => -rng.Next(1, 10),
                    StockMovementType.Adjustment => rng.Next(-10, 11) is var a && a == 0 ? 1 : a,
                    _ => rng.Next(1, 10),
                };
                await service.ApplyAsync(Tenant, location, item,
                    DeterministicGuid.ForItem(BusinessId, item), type, qty, "prop-test", null, null);
            }
            await db.SaveChangesAsync();
        }

        using var check = Ctx(conn);
        foreach (var item in items)
        {
            var ledger = await check.StockMovements.Where(m => m.ItemIdOne == item).SumAsync(m => m.QtyDelta);
            var level = await check.StockLevels.Where(l => l.ItemIdOne == item).Select(l => l.Quantity).SingleAsync();
            Assert.Equal(ledger, level);
        }
    }

    [Fact]
    public async Task Sale_consumer_moves_stock_and_is_idempotent_under_replay()
    {
        using var conn = OpenSeeded();
        var saleId = Uuid7.New();
        using (var ctx = Ctx(conn)) { RecordSale(ctx, saleId, "GAME-9", 3); ctx.SaveChanges(); }

        await DrainAsync(conn);
        // crash-before-offset-advance redelivery: wipe the offset, drain again
        using (var ctx = Ctx(conn))
        {
            var off = await ctx.ConsumerOffsets.FirstAsync(o => o.ConsumerName == StockProjectionConsumer.ConsumerName);
            off.LastOutboxId = 0;
            await ctx.SaveChangesAsync();
        }
        await DrainAsync(conn);

        using var check = Ctx(conn);
        var movement = Assert.Single(await check.StockMovements.ToListAsync());
        Assert.Equal(StockMovementType.Sale, movement.Type);
        Assert.Equal(-3, movement.QtyDelta);
        Assert.Equal(saleId, movement.RefId);
        Assert.Equal(-3, (await check.StockLevels.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task Return_line_restocks()
    {
        using var conn = OpenSeeded();
        using (var ctx = Ctx(conn)) { RecordSale(ctx, Uuid7.New(), "GAME-9", -2); ctx.SaveChanges(); } // return of 2

        await DrainAsync(conn);

        using var check = Ctx(conn);
        var movement = Assert.Single(await check.StockMovements.ToListAsync());
        Assert.Equal(StockMovementType.Return, movement.Type);
        Assert.Equal(2, movement.QtyDelta);
        Assert.Equal(2, (await check.StockLevels.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task Rebuild_reproduces_levels_from_the_ledger()
    {
        using var conn = OpenSeeded();
        using (var db = Ctx(conn))
        {
            var service = new StockLedgerService(db);
            var location = await service.EnsureStoreLocationAsync(Tenant, StoreId);
            await service.ApplyAsync(Tenant, location, "X", Guid.NewGuid(), StockMovementType.Receipt, 10, null, null, null);
            await service.ApplyAsync(Tenant, location, "X", Guid.NewGuid(), StockMovementType.Sale, -4, null, null, null);
            await service.ApplyAsync(Tenant, location, "Y", Guid.NewGuid(), StockMovementType.Adjustment, 7, "count", null, null);
            await db.SaveChangesAsync();
        }

        // corrupt the materialised levels, then rebuild
        using (var db = Ctx(conn))
        {
            foreach (var l in await db.StockLevels.ToListAsync()) l.Quantity = 999;
            await db.SaveChangesAsync();
            await StockRebuilder.RebuildLevelsAsync(db, Tenant);
        }

        using var check = Ctx(conn);
        Assert.Equal(6, (await check.StockLevels.SingleAsync(l => l.ItemIdOne == "X")).Quantity);
        Assert.Equal(7, (await check.StockLevels.SingleAsync(l => l.ItemIdOne == "Y")).Quantity);
    }

    [Fact]
    public async Task Seed_heals_history_replayed_before_adoption_and_fences_unprocessed_history()
    {
        using var conn = OpenSeeded();

        // A pipeline sale exists BEFORE adoption; the bridge has already decremented the
        // legacy Stocks value (47 → 45), which is what the opening will capture.
        var processedSale = Uuid7.New();
        using (var ctx = Ctx(conn)) { RecordSale(ctx, processedSale, "GAME-9", 2); ctx.SaveChanges(); }
        await DrainAsync(conn); // consumer replays history first (the wrong-order case)

        // an UNPROCESSED historic event also sits in the outbox at seed time
        using (var ctx = Ctx(conn)) { RecordSale(ctx, Uuid7.New(), "GAME-9", 1); ctx.SaveChanges(); }

        using (var ctx = Ctx(conn))
        {
            ctx.Stocks.Add(new Stock { IdOne = "GAME-9", IdTwo = BusinessId, IdThree = StoreId, Quantity = 45 });
            ctx.SaveChanges();
        }
        using (var db = Ctx(conn)) await StockRebuilder.SeedOpeningBalancesAsync(db, Tenant);
        await DrainAsync(conn); // fenced history must NOT apply

        using (var check = Ctx(conn))
        {
            // ledger == legacy: the replayed −2 was healed, the unprocessed −1 was fenced
            Assert.Equal(45, (await check.StockLevels.SingleAsync(l => l.ItemIdOne == "GAME-9")).Quantity);
            var movement = Assert.Single(await check.StockMovements.ToListAsync());
            Assert.Equal(StockRebuilder.OpeningReason, movement.Reason);
        }

        // post-adoption sales flow normally
        using (var ctx = Ctx(conn)) { RecordSale(ctx, Uuid7.New(), "GAME-9", 3); ctx.SaveChanges(); }
        await DrainAsync(conn);
        using (var check = Ctx(conn))
            Assert.Equal(42, (await check.StockLevels.SingleAsync(l => l.ItemIdOne == "GAME-9")).Quantity);
    }

    [Fact]
    public async Task Opening_balance_seed_is_idempotent_and_matches_legacy_stocks()
    {
        using var conn = OpenSeeded();
        using (var ctx = Ctx(conn))
        {
            ctx.Stocks.Add(new Stock { IdOne = "GAME-9", IdTwo = BusinessId, IdThree = StoreId, Quantity = 47 });
            ctx.Stocks.Add(new Stock { IdOne = "BRUSH-1", IdTwo = BusinessId, IdThree = StoreId, Quantity = 7 });
            ctx.Stocks.Add(new Stock { IdOne = "ZERO-0", IdTwo = BusinessId, IdThree = StoreId, Quantity = 0 }); // skipped
            ctx.SaveChanges();
        }

        using (var db = Ctx(conn)) Assert.Equal(2, await StockRebuilder.SeedOpeningBalancesAsync(db, Tenant));
        using (var db = Ctx(conn)) Assert.Equal(0, await StockRebuilder.SeedOpeningBalancesAsync(db, Tenant)); // re-run = no-op

        using var check = Ctx(conn);
        Assert.Equal(47, (await check.StockLevels.SingleAsync(l => l.ItemIdOne == "GAME-9")).Quantity);
        Assert.Equal(7, (await check.StockLevels.SingleAsync(l => l.ItemIdOne == "BRUSH-1")).Quantity);
        Assert.Equal(2, await check.StockMovements.CountAsync());
    }

    /// <summary>
    /// FE5.5: an item flagged "don't track stock" (carrier bags, back-issues) posts NO stock
    /// movement when sold — but the sale and its line are recorded exactly as normal, so
    /// items-sold / best-seller reporting is untouched. A tracked item in the SAME basket still
    /// moves, which is the case a naive "skip the whole sale" implementation would break.
    /// </summary>
    [Fact]
    public async Task An_untracked_item_records_the_sale_but_never_moves_stock()
    {
        using var conn = OpenSeeded();
        using (var ctx = Ctx(conn))
        {
            ctx.Items.Add(new Item
            {
                IdOne = "BAG-1", IdTwo = BusinessId, Name = "Carrier bag", Brand = "-", Desc = "-",
                Cost = 0, ExPrice = 0.04m, Price = 0.05m, TaxId = 1, CatId = Uuid7.New(),
                StockUntracked = true,
            });
            ctx.Items.Add(new Item
            {
                IdOne = "COMIC-1", IdTwo = BusinessId, Name = "A comic", Brand = "-", Desc = "-",
                Cost = 1, ExPrice = 1, Price = 1.2m, TaxId = 1, CatId = Uuid7.New(),
                StockUntracked = false,
            });
            ctx.SaveChanges();
        }

        // one sale per item (the harness writes a single line per sale)
        using (var ctx = Ctx(conn))
        {
            RecordSale(ctx, Uuid7.New(), "BAG-1", 3);
            RecordSale(ctx, Uuid7.New(), "COMIC-1", 2);
            await ctx.SaveChangesAsync();
        }
        await DrainAsync(conn);

        using var check = Ctx(conn);
        // the untracked item has NO level and NO movement…
        Assert.Empty(check.StockLevels.Where(l => l.ItemIdOne == "BAG-1"));
        Assert.Empty(check.StockMovements.Where(m => m.ItemIdOne == "BAG-1"));
        // …while the tracked item in the same batch moved normally
        Assert.Equal(-2, (await check.StockLevels.SingleAsync(l => l.ItemIdOne == "COMIC-1")).Quantity);
        // and BOTH sales + their lines are recorded, so reporting sees the bags
        Assert.Equal(2, await check.SalesV2.CountAsync());
        Assert.Equal(1, await check.SaleLines.CountAsync(l => l.DiscountsJson.Contains("BAG-1")));
    }
}
