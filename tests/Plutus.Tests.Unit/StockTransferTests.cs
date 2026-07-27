using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Catalogue;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP5.2 (DoD): a transfer never double-counts — while in transit the goods are in NEITHER
/// location (total on-hand drops by qty and is restored on receive; cancel restores at the
/// source); stock takes post counted-vs-expected adjustments and zero deltas move nothing.
/// </summary>
public class StockTransferTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid BusinessId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(Tenant)) { CurrentUser = "transfer-test" };

    private static T WithActor<T>(T controller) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, ActorId.ToString()) }, "test")),
            },
        };
        return controller;
    }

    private static async Task<(SqliteConnection Conn, Guid FromId, Guid ToId)> OpenWithTwoLocationsAsync()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        Guid fromId, toId;
        using (var ctx = Ctx(conn))
        {
            ctx.Database.EnsureCreated();
            ctx.Business.Add(new Business { Id = BusinessId, Name = "Testco", NameAbbr = "TST", VatIN = "GB0" });
            ctx.Items.Add(new Item
            {
                IdOne = "GAME-9", IdTwo = BusinessId, Name = "Game", Brand = "-", Desc = "",
                Cost = 1, ExPrice = 1, Price = 1.2m, TaxId = 1, CatId = Guid.NewGuid(),
            });
            ctx.SaveChanges();

            var service = new StockLedgerService(ctx);
            var from = await service.EnsureStoreLocationAsync(Tenant, 1);
            var to = new StockLocation { Id = Uuid7.New(), TenantId = Tenant, StoreId = 2, Type = StockLocationType.Store, Name = "Store 2" };
            ctx.StockLocations.Add(to);
            await service.ApplyAsync(Tenant, from, "GAME-9",
                DeterministicGuid.ForItem(BusinessId, "GAME-9"), StockMovementType.Receipt, 10, null, null, null);
            await ctx.SaveChangesAsync();
            fromId = from.Id;
            toId = to.Id;
        }
        return (conn, fromId, toId);
    }

    private static async Task<int> TotalOnHand(SqliteConnection conn)
    {
        using var ctx = Ctx(conn);
        return await ctx.StockLevels.SumAsync(l => l.Quantity);
    }

    [Fact]
    public async Task Transfer_never_double_counts_in_transit_then_receive()
    {
        var (conn, fromId, toId) = await OpenWithTwoLocationsAsync();
        using var _ = conn;
        Assert.Equal(10, await TotalOnHand(conn));

        Guid transferId;
        using (var db = Ctx(conn))
        {
            var res = await WithActor(new StockTransfersController(db, new FixedTenantContext(Tenant)))
                .Create(new TransferBody(fromId, toId, "GAME-9", 4, "restock branch"));
            var created = Assert.IsType<CreatedResult>(res);
            transferId = (Guid)created.Value!.GetType().GetProperty("id")!.GetValue(created.Value)!;
        }

        // in transit: source down 4, destination untouched, total = 6 — nowhere twice
        using (var check = Ctx(conn))
        {
            Assert.Equal(6, (await check.StockLevels.SingleAsync(l => l.StockLocationId == fromId)).Quantity);
            Assert.False(await check.StockLevels.AnyAsync(l => l.StockLocationId == toId && l.Quantity != 0));
        }
        Assert.Equal(6, await TotalOnHand(conn));

        using (var db = Ctx(conn))
            Assert.IsType<NoContentResult>(await WithActor(new StockTransfersController(db, new FixedTenantContext(Tenant))).Receive(transferId));

        using (var check = Ctx(conn))
        {
            Assert.Equal(6, (await check.StockLevels.SingleAsync(l => l.StockLocationId == fromId)).Quantity);
            Assert.Equal(4, (await check.StockLevels.SingleAsync(l => l.StockLocationId == toId)).Quantity);
        }
        Assert.Equal(10, await TotalOnHand(conn)); // conserved

        // receive twice → 409, nothing moves
        using (var db = Ctx(conn))
            Assert.IsType<ConflictObjectResult>(await WithActor(new StockTransfersController(db, new FixedTenantContext(Tenant))).Receive(transferId));
        Assert.Equal(10, await TotalOnHand(conn));
    }

    [Fact]
    public async Task Cancel_restores_the_source()
    {
        var (conn, fromId, toId) = await OpenWithTwoLocationsAsync();
        using var _ = conn;

        Guid transferId;
        using (var db = Ctx(conn))
        {
            var created = Assert.IsType<CreatedResult>(await WithActor(new StockTransfersController(db, new FixedTenantContext(Tenant)))
                .Create(new TransferBody(fromId, toId, "GAME-9", 3, null)));
            transferId = (Guid)created.Value!.GetType().GetProperty("id")!.GetValue(created.Value)!;
        }
        using (var db = Ctx(conn))
            Assert.IsType<NoContentResult>(await WithActor(new StockTransfersController(db, new FixedTenantContext(Tenant))).Cancel(transferId));

        using var check = Ctx(conn);
        Assert.Equal(10, (await check.StockLevels.SingleAsync(l => l.StockLocationId == fromId)).Quantity);
        Assert.Equal(StockTransferStatus.Cancelled, (await check.StockTransfers.SingleAsync()).Status);
        // ledger holds the full story: receipt +10, out −3, back +3
        Assert.Equal(3, await check.StockMovements.CountAsync());
    }

    [Fact]
    public async Task Stock_take_posts_counted_vs_expected_adjustments_only_for_deltas()
    {
        var (conn, fromId, _) = await OpenWithTwoLocationsAsync();
        using var _c = conn;

        using (var db = Ctx(conn))
        {
            var res = await WithActor(new StockTransfersController(db, new FixedTenantContext(Tenant)))
                .StockTake(new StockTakeBody(fromId, null, "quarterly count", new List<StockTakeCount>
                {
                    new("GAME-9", 8),      // expected 10 → −2
                }));
            Assert.IsType<OkObjectResult>(res);
        }

        using (var check = Ctx(conn))
        {
            Assert.Equal(8, (await check.StockLevels.SingleAsync(l => l.StockLocationId == fromId)).Quantity);
            var adj = Assert.Single(await check.StockMovements
                .Where(m => m.Type == StockMovementType.Adjustment).ToListAsync());
            Assert.Equal(-2, adj.QtyDelta);
            Assert.Contains("counted 8, expected 10", adj.Reason);
            Assert.Contains("quarterly count", adj.Reason);
        }

        // zero-delta count: no new movement
        using (var db = Ctx(conn))
        {
            await WithActor(new StockTransfersController(db, new FixedTenantContext(Tenant)))
                .StockTake(new StockTakeBody(fromId, null, null, new List<StockTakeCount> { new("GAME-9", 8) }));
        }
        using (var check = Ctx(conn))
            Assert.Equal(1, await check.StockMovements.CountAsync(m => m.Type == StockMovementType.Adjustment));
    }
}
