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

/// <summary>WP5.3: receiving a PO posts RECEIPT movements (partials → PartiallyReceived →
/// Received), the door cost overrides the ordered cost, over-receiving is rejected, and
/// levels track the receipts through the WP5.1 ledger.</summary>
public class GoodsInTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid BusinessId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(Tenant)) { CurrentUser = "goodsin-test" };

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

    private static async Task<(SqliteConnection Conn, Guid SupplierId)> OpenAsync()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        Guid supplierId;
        using (var ctx = Ctx(conn))
        {
            ctx.Database.EnsureCreated();
            ctx.Business.Add(new Business { Id = BusinessId, Name = "Testco", NameAbbr = "TST", VatIN = "GB0" });
            ctx.Stores.Add(new Store
            {
                Id = 1, BusinessId = BusinessId, ContactNumber = "-",
                AdLine1 = "-", AdLine2 = "", City = "-", PostCode = "-", Country = "-",
            });
            ctx.Items.Add(new Item
            {
                IdOne = "GAME-9", IdTwo = BusinessId, Name = "Game", Brand = "-", Desc = "",
                Cost = 1, ExPrice = 1, Price = 1.2m, TaxId = 1, CatId = Guid.NewGuid(),
            });
            var supplier = new Supplier { Id = Uuid7.New(), TenantId = Tenant, Name = "Asmodee", Active = true, CreatedAtUtc = DateTime.UtcNow };
            ctx.Suppliers.Add(supplier);
            ctx.SaveChanges();
            supplierId = supplier.Id;
        }
        return (conn, supplierId);
    }

    private static GoodsInController Controller(MySqlDbContext db) =>
        WithActor(new GoodsInController(db, new FixedTenantContext(Tenant)));

    [Fact]
    public async Task Partial_then_final_receipt_moves_stock_captures_cost_and_tracks_status()
    {
        var (conn, supplierId) = await OpenAsync();
        using var _ = conn;

        Guid poId, lineId;
        using (var db = Ctx(conn))
        {
            var created = Assert.IsType<CreatedResult>(await Controller(db).Create(new CreatePOBody(
                supplierId, 1, "PO-1001", null,
                new List<POLineBody> { new("GAME-9", 10, 450) })));
            poId = (Guid)created.Value!.GetType().GetProperty("id")!.GetValue(created.Value)!;
        }
        using (var check = Ctx(conn))
            lineId = (await check.POLines.SingleAsync()).Id;

        // partial receipt of 4 at a different door cost
        using (var db = Ctx(conn))
        {
            var res = await Controller(db).Receive(poId, new ReceiveBody(
                new List<ReceiveLine> { new(lineId, 4, 475) }));
            Assert.IsType<OkObjectResult>(res);
        }
        using (var check = Ctx(conn))
        {
            Assert.Equal(PurchaseOrderStatus.PartiallyReceived, (await check.PurchaseOrders.SingleAsync()).Status);
            var line = await check.POLines.SingleAsync();
            Assert.Equal(4, line.QtyReceived);
            Assert.Equal(475, line.UnitCostPence); // door cost captured
            var movement = Assert.Single(await check.StockMovements.ToListAsync());
            Assert.Equal(StockMovementType.Receipt, movement.Type);
            Assert.Equal(4, movement.QtyDelta);
            Assert.Equal(poId, movement.RefId);
            Assert.Contains("PO-1001", movement.Reason);
            Assert.Equal(4, (await check.StockLevels.SingleAsync()).Quantity);
        }

        // over-receive rejected: outstanding is 6
        using (var db = Ctx(conn))
            Assert.IsType<BadRequestObjectResult>(await Controller(db).Receive(poId, new ReceiveBody(
                new List<ReceiveLine> { new(lineId, 7, null) })));

        // final receipt of 6 completes the order
        using (var db = Ctx(conn))
            Assert.IsType<OkObjectResult>(await Controller(db).Receive(poId, new ReceiveBody(
                new List<ReceiveLine> { new(lineId, 6, null) })));
        using (var check = Ctx(conn))
        {
            Assert.Equal(PurchaseOrderStatus.Received, (await check.PurchaseOrders.SingleAsync()).Status);
            Assert.Equal(10, (await check.StockLevels.SingleAsync()).Quantity);
            // and receiving against a completed order is a 409
        }
        using (var db = Ctx(conn))
            Assert.IsType<ConflictObjectResult>(await Controller(db).Receive(poId, new ReceiveBody(
                new List<ReceiveLine> { new(lineId, 1, null) })));
    }

    [Fact]
    public async Task Cancel_only_from_open_and_unknown_items_rejected()
    {
        var (conn, supplierId) = await OpenAsync();
        using var _ = conn;

        // unknown item on create
        using (var db = Ctx(conn))
            Assert.IsType<BadRequestObjectResult>(await Controller(db).Create(new CreatePOBody(
                supplierId, 1, null, null, new List<POLineBody> { new("NOPE", 1, 100) })));

        Guid poId;
        using (var db = Ctx(conn))
        {
            var created = Assert.IsType<CreatedResult>(await Controller(db).Create(new CreatePOBody(
                supplierId, 1, null, null, new List<POLineBody> { new("GAME-9", 5, 100) })));
            poId = (Guid)created.Value!.GetType().GetProperty("id")!.GetValue(created.Value)!;
        }
        using (var db = Ctx(conn))
            Assert.IsType<NoContentResult>(await Controller(db).Cancel(poId));
        using (var db = Ctx(conn))
            Assert.IsType<ConflictObjectResult>(await Controller(db).Cancel(poId)); // already cancelled
        using (var check = Ctx(conn))
            Assert.Empty(await check.StockMovements.ToListAsync()); // nothing ever moved
    }
}
