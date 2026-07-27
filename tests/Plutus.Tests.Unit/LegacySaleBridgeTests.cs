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
/// Phase-2 legacy bridge (WP2.1 decision 2026-07-24): a SaleRecorded event is projected into
/// the legacy Sales/Trans/PaySales/Transaction_Discounts/CheckoutItemChange/Refunds tables plus
/// a Stocks adjustment, so the legacy read model keeps working while clients write only v1.
/// FK enforcement is off in these tests (Foreign Keys=False) so the projection logic is
/// testable without seeding the full catalogue graph — on MySQL the referenced rows always
/// exist because sold items come from the catalogue and operators from login.
/// </summary>
public class LegacySaleBridgeTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid BusinessId = Guid.NewGuid();
    private static readonly Guid TillId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private const int StoreId = 7;
    private const int CashPayId = 2;

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(Tenant)) { CurrentUser = "bridge-test" };

    private static SqliteConnection OpenSeeded()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Ctx(conn);
        ctx.Database.EnsureCreated();

        ctx.Business.Add(new Business { Id = BusinessId, Name = "Testco", NameAbbr = "TST", VatIN = "GB0" });
        ctx.Till.Add(new Till { Id = TillId, StoreId = StoreId, LastOnline = DateTime.UtcNow });
        ctx.Employees.Add(new Employee
        {
            Id = EmployeeId, BusinessId = BusinessId, StoreId = StoreId, Active = true,
            Email = "-", FName = "Test", LName = "Op", Mobile = "-", NIN = "-",
            AdLine1 = "-", AdLine2 = "", City = "-", PostCode = "-", Country = "-",
        });
        ctx.PayMethods.Add(new PaymentMethod { Id = 1, Name = "Card", IsChangeable = false });
        ctx.PayMethods.Add(new PaymentMethod { Id = CashPayId, Name = "Cash", IsChangeable = true });
        ctx.Stocks.Add(new Stock { IdOne = "ITEM-1", IdTwo = BusinessId, IdThree = StoreId, Quantity = 10 });
        ctx.SaveChanges();
        return conn;
    }

    private static string Meta(string itemIdOne, long exUnitPence, object[] discounts = null, Guid? originSaleId = null)
        => JsonSerializer.Serialize(new
        {
            itemIdOne,
            exUnitPence,
            discounts,
            @return = originSaleId.HasValue ? new { originSaleId = originSaleId.Value } : null,
        });

    /// <summary>A web-POS shaped SaleV2: 2× ITEM-1 @ £1.20 with 10% discount (24p), paid cash
    /// £3.00 with 84p change. Gross 216p, VAT 36p (line ex 180p via ex-unit 100p − apportioned).</summary>
    private static SaleV2 WebPosSale(Guid saleId)
    {
        var lines = new List<SaleLine>
        {
            new()
            {
                Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId, LineNo = 1,
                ItemId = DeterministicGuid.ForItem(BusinessId, "ITEM-1"), Qty = 2,
                UnitPricePence = 120, DiscountPence = 24, LineGrossPence = 216,
                VatRateBp = 2000, VatAmountPence = 36,
                DiscountsJson = Meta("ITEM-1", 100, new object[] { new { id = 5, rate = 0.10 } }),
            },
        };
        var tenders = new List<SaleTender>
        {
            new()
            {
                Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId,
                TenderType = TenderType.Cash, AmountPence = 300, ChangePence = 84,
                ProviderRef = JsonSerializer.Serialize(new { payId = CashPayId }),
            },
        };
        return SaleV2.Create(saleId, Tenant, TillId, Guid.NewGuid(), 1, SaleChannel.WebPos,
            new DateOnly(2026, 7, 24), DateTime.UtcNow, DateTime.UtcNow, 216, 36, lines, tenders,
            operatorUserId: EmployeeId);
    }

    private static SaleRecorded EventFor(SaleV2 s) =>
        new(Uuid7.New(), s.TenantId, s.OccurredAtUtc, s.Id, s.DeviceId, s.DeviceSeq, s.BusinessDay);

    private static async Task HandleAndSave(SqliteConnection conn, SaleV2 sale)
    {
        using var ctx = Ctx(conn);
        await new LegacySaleBridgeConsumer(ctx).HandleAsync(EventFor(sale), default);
        await ctx.SaveChangesAsync(); // the drainer's commit
    }

    [Fact]
    public async Task Projects_sale_lines_discounts_tender_and_stock()
    {
        using var conn = OpenSeeded();
        var sale = WebPosSale(Uuid7.New());
        using (var ctx = Ctx(conn)) { ctx.SalesV2.Add(sale); ctx.SaveChanges(); }

        await HandleAndSave(conn, sale);

        using var check = Ctx(conn);
        var legacy = await check.Sales.Include(s => s.Transactions).ThenInclude(t => t.Transaction_Discounts)
            .Include(s => s.PaySales).FirstAsync(s => s.Id == sale.Id);

        Assert.Equal(2.16m, legacy.Total);
        Assert.Equal(1.80m, legacy.TotalExTax);
        Assert.Equal(EmployeeId, legacy.EmployeeId);
        Assert.Equal(StoreId, legacy.StoreId);
        Assert.Equal(TillId, legacy.TillId);

        var t = Assert.Single(legacy.Transactions);
        Assert.Equal(2, t.Amount);
        Assert.Equal(1.20m, t.ItemCostPrice);
        Assert.Equal(1.00m, t.ItemCostExPrice);
        Assert.Equal("ITEM-1", t.ItemIdOne);
        Assert.Equal(BusinessId, t.ItemIdTwo);
        var d = Assert.Single(t.Transaction_Discounts);
        Assert.Equal(5, d.DiscountId);
        Assert.Equal(0.10m, d.DiscountRate);

        var p = Assert.Single(legacy.PaySales);
        Assert.Equal(CashPayId, p.PayId);
        Assert.Equal(3.00m, p.Amount);
        Assert.Equal(0.84m, p.Change);

        var stock = await check.Stocks.FirstAsync(s => s.IdOne == "ITEM-1");
        Assert.Equal(8, stock.Quantity); // 10 − 2
    }

    [Fact]
    public async Task Adjusted_price_line_creates_a_CheckoutItemChange()
    {
        using var conn = OpenSeeded();
        var saleId = Uuid7.New();
        var line = new SaleLine
        {
            Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId, LineNo = 1,
            ItemId = DeterministicGuid.ForItem(BusinessId, "ITEM-1"), Qty = 1,
            UnitPricePence = 90, DiscountPence = 0, LineGrossPence = 90,
            VatRateBp = 2000, VatAmountPence = 15, OverriddenFromPence = 120,
            DiscountsJson = Meta("ITEM-1", 75),
        };
        var tender = new SaleTender
        {
            Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId,
            TenderType = TenderType.Card, AmountPence = 90, ChangePence = 0,
            ProviderRef = JsonSerializer.Serialize(new { payId = 1 }),
        };
        var sale = SaleV2.Create(saleId, Tenant, TillId, Guid.NewGuid(), 2, SaleChannel.WebPos,
            new DateOnly(2026, 7, 24), DateTime.UtcNow, DateTime.UtcNow, 90, 15,
            new[] { line }, new[] { tender }, operatorUserId: EmployeeId);
        using (var ctx = Ctx(conn)) { ctx.SalesV2.Add(sale); ctx.SaveChanges(); }

        await HandleAndSave(conn, sale);

        using var check = Ctx(conn);
        var t = await check.Trans.Include(x => x.CheckoutItemChange).FirstAsync(x => x.IdTwo == saleId);
        Assert.NotNull(t.CheckoutItemChange);
        Assert.Equal(0.90m, t.CheckoutItemChange.Price);
        Assert.Equal(0.75m, t.CheckoutItemChange.ExPrice);
    }

    [Fact]
    public async Task Return_line_creates_a_Refund_and_restocks()
    {
        using var conn = OpenSeeded();

        // The original sale being returned against (already projected).
        var originId = Uuid7.New();
        using (var ctx = Ctx(conn))
        {
            ctx.Sales.Add(new Sale
            {
                Id = originId, Total = 1.20m, TotalExTax = 1.00m, DateOfSale = DateTime.UtcNow,
                EmployeeId = EmployeeId, StoreId = StoreId, TillId = TillId,
            });
            ctx.SaveChanges();
        }

        var saleId = Uuid7.New();
        var lines = new List<SaleLine>
        {
            new() // one item sold
            {
                Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId, LineNo = 1,
                ItemId = DeterministicGuid.ForItem(BusinessId, "ITEM-1"), Qty = 1,
                UnitPricePence = 120, DiscountPence = 0, LineGrossPence = 120,
                VatRateBp = 2000, VatAmountPence = 20, DiscountsJson = Meta("ITEM-1", 100),
            },
            new() // one item returned: negative qty by WP2.1 convention
            {
                Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId, LineNo = 2,
                ItemId = DeterministicGuid.ForItem(BusinessId, "ITEM-1"), Qty = -1,
                UnitPricePence = 120, DiscountPence = 0, LineGrossPence = -120,
                VatRateBp = 2000, VatAmountPence = -20,
                DiscountsJson = Meta("ITEM-1", 100, originSaleId: originId),
            },
        };
        var sale = SaleV2.Create(saleId, Tenant, TillId, Guid.NewGuid(), 3, SaleChannel.WebPos,
            new DateOnly(2026, 7, 24), DateTime.UtcNow, DateTime.UtcNow, 0, 0, lines,
            new[] { new SaleTender { Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId, TenderType = TenderType.Cash, AmountPence = 0, ChangePence = 0 } },
            operatorUserId: EmployeeId);
        using (var ctx = Ctx(conn)) { ctx.SalesV2.Add(sale); ctx.SaveChanges(); }

        await HandleAndSave(conn, sale);

        using var check = Ctx(conn);
        var legacy = await check.Sales.Include(s => s.Transactions).Include(s => s.Refunds)
            .FirstAsync(s => s.Id == saleId);
        Assert.Single(legacy.Transactions); // only the sold line
        var r = Assert.Single(legacy.Refunds);
        Assert.Equal(1, r.Amount);
        Assert.Equal("ITEM-1", r.ItemIdOne);
        Assert.Equal(originId, r.SaleIdReturned);
        Assert.Equal(EmployeeId, r.AuthoriserId);

        var stock = await check.Stocks.FirstAsync(s => s.IdOne == "ITEM-1");
        Assert.Equal(10, stock.Quantity); // −1 sold, +1 returned
    }

    [Fact]
    public async Task Replaying_the_event_does_not_duplicate_the_projection()
    {
        using var conn = OpenSeeded();
        var sale = WebPosSale(Uuid7.New());
        using (var ctx = Ctx(conn)) { ctx.SalesV2.Add(sale); ctx.SaveChanges(); }

        await HandleAndSave(conn, sale);
        await HandleAndSave(conn, sale); // replay (e.g. redelivery before offset advanced)

        using var check = Ctx(conn);
        Assert.Equal(1, await check.Sales.CountAsync(s => s.Id == sale.Id));
        Assert.Equal(1, await check.Trans.CountAsync());
        Assert.Equal(8, (await check.Stocks.FirstAsync()).Quantity); // stock applied once
    }

    [Fact]
    public async Task Sales_without_projection_metadata_are_skipped()
    {
        using var conn = OpenSeeded();
        var saleId = Uuid7.New();
        var line = new SaleLine
        {
            Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId, LineNo = 1,
            ItemId = Guid.NewGuid(), Qty = 1, UnitPricePence = 100, DiscountPence = 0,
            LineGrossPence = 100, VatRateBp = 2000, VatAmountPence = 17, DiscountsJson = null,
        };
        var sale = SaleV2.Create(saleId, Tenant, TillId, Guid.NewGuid(), 4, SaleChannel.Till,
            new DateOnly(2026, 7, 24), DateTime.UtcNow, DateTime.UtcNow, 100, 17,
            new[] { line }, new[] { new SaleTender { Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId, TenderType = TenderType.Cash, AmountPence = 100, ChangePence = 0 } });
        using (var ctx = Ctx(conn)) { ctx.SalesV2.Add(sale); ctx.SaveChanges(); }

        await HandleAndSave(conn, sale);

        using var check = Ctx(conn);
        Assert.Equal(0, await check.Sales.CountAsync());
    }

    [Fact]
    public async Task Drainer_commits_projection_atomically_with_offset_and_processed_marker()
    {
        using var conn = OpenSeeded();
        var sale = WebPosSale(Uuid7.New());
        var evt = EventFor(sale);
        using (var ctx = Ctx(conn))
        {
            ctx.SalesV2.Add(sale);
            ctx.OutboxEvents.Add(new OutboxEvent
            {
                EventId = evt.EventId, TenantId = Tenant, EventType = nameof(SaleRecorded),
                PayloadJson = JsonSerializer.Serialize(evt), CreatedAtUtc = DateTime.UtcNow,
            });
            ctx.SaveChanges();
        }

        var drainer = new OutboxDrainer(new DefaultOutboxEventCodec(),
            new OutboxDispatcherOptions { RetryBackoffs = Array.Empty<TimeSpan>() });
        using (var db = Ctx(conn))
        {
            // Same context instance for drainer and consumer — the production DI wiring.
            var stats = await drainer.DrainConsumerAsync(db, new LegacySaleBridgeConsumer(db), default);
            Assert.Equal(1, stats.Handled);
        }

        using var check = Ctx(conn);
        Assert.Equal(1, await check.Sales.CountAsync(s => s.Id == sale.Id));
        Assert.Equal(1, await check.ProcessedEvents.CountAsync(p => p.ConsumerName == LegacySaleBridgeConsumer.ConsumerName));
        Assert.Equal(1, (await check.ConsumerOffsets.FirstAsync(o => o.ConsumerName == LegacySaleBridgeConsumer.ConsumerName)).LastOutboxId);
    }

    [Fact]
    public void Deterministic_item_guid_is_stable_and_well_formed()
    {
        var a = DeterministicGuid.ForItem(BusinessId, "ITEM-1");
        var b = DeterministicGuid.ForItem(BusinessId, "ITEM-1");
        var c = DeterministicGuid.ForItem(BusinessId, "ITEM-2");

        Assert.Equal(a, b);                 // stable
        Assert.NotEqual(a, c);              // key-sensitive
        var s = a.ToString("D");
        Assert.Equal('8', s[14]);           // version nibble 8 (custom)
        Assert.Contains(s[19], "89ab");     // RFC variant

        // Cross-language parity vector — the TS twin (web POS src/pipeline.ts itemGuid)
        // produced exactly this id for these inputs (verified in the 2026-07-24 smoke test).
        Assert.Equal(Guid.Parse("4abfb7bf-50d6-8990-9a56-b4ebfafbe22e"), DeterministicGuid.ForItem(
            Guid.Parse("d5a31aac-159e-9a30-706b-02f9eb935600"), "TEST-ITEM"));
        Assert.Equal(Guid.Parse("4abfb7bf-50d6-8990-9a56-b4ebfafbe22e"), DeterministicGuid.ForItem(
            Guid.Parse("D5A31AAC-159E-9A30-706B-02F9EB935600"), "TEST-ITEM")); // case-insensitive on the guid
    }
}
