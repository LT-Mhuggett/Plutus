using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.Reporting;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>WP12.2: the v1 sale-detail endpoint is enriched with per-line item NAMES, the
/// operator NAME, and refund ADJUSTMENTS — the additions the till needs to read this instead of
/// the legacy /api/Sale/Detail (bridge-fed). The portal's existing fields must be untouched.</summary>
public class SaleDetailEnrichmentTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Biz = Guid.NewGuid();

    private static MySqlDbContext Ctx(SqliteConnection conn) =>
        new(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
            new FixedTenantContext(Tenant)) { CurrentUser = "t" };

    [Fact]
    public async Task Sale_detail_carries_item_names_operator_name_and_refund_adjustments()
    {
        using var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        var saleId = Uuid7.New();
        var operatorId = Uuid7.New();
        using (var db = Ctx(conn))
        {
            db.Database.EnsureCreated();
            db.Business.Add(new Business { Id = Biz, Name = "Testco", NameAbbr = "T", VatIN = "GB0" });
            db.Items.Add(new Item { IdOne = "5011921156993", IdTwo = Biz, Name = "Orks: Lootas", Brand = "GW", Desc = "", Cost = 1, ExPrice = 1, Price = 20.79m, TaxId = 1, CatId = Guid.NewGuid() });
            db.People.Add(new Person { Id = operatorId, FName = "Sam", LName = "Till", Email = "sam@test.local", Mobile = "0", AdLine1 = "-", AdLine2 = "", City = "-", PostCode = "T1", Country = "-" });

            var line = new SaleLine
            {
                Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId, LineNo = 1,
                ItemId = DeterministicGuid.ForItem(Biz, "5011921156993"), ItemIdOne = "5011921156993", Qty = 1,
                UnitPricePence = 2079, LineGrossPence = 2079, DiscountPence = 0, VatRateBp = 2000, VatAmountPence = 347,
            };
            var tender = new SaleTender { Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId, TenderType = TenderType.Card, AmountPence = 2079, ChangePence = 0 };
            db.SalesV2.Add(SaleV2.Create(saleId, Tenant, Uuid7.New(), Uuid7.New(), 1, SaleChannel.Till,
                new DateOnly(2026, 7, 1), DateTime.UtcNow, DateTime.UtcNow, 2079, 347,
                new[] { line }, new[] { tender }, operatorUserId: operatorId, note: "test sale"));
            db.SaleAdjustments.Add(new SaleAdjustment
            {
                Id = Uuid7.New(), TenantId = Tenant, Type = AdjustmentType.Refund, OriginalSaleId = saleId,
                AmountPence = 500, Reason = "damaged", CreatedAtUtc = DateTime.UtcNow,
            });
            db.SaveChanges();
        }

        using var ctx = Ctx(conn);
        var controller = new ReportsController(ctx, new FixedTenantContext(Tenant));
        var result = await controller.SaleDetail(saleId) as OkObjectResult;
        Assert.NotNull(result);

        // Reflect over the anonymous response (the shape the frontends read).
        var body = result!.Value!;
        string S(string p) => body.GetType().GetProperty(p)!.GetValue(body)?.ToString();
        Assert.Equal("Sam Till", S("operatorName"));

        var lines = ((System.Collections.IEnumerable)body.GetType().GetProperty("lines")!.GetValue(body)!).Cast<object>().ToList();
        var l0 = lines[0];
        string LS(string p) => l0.GetType().GetProperty(p)!.GetValue(l0)?.ToString();
        Assert.Equal("Orks: Lootas", LS("itemName"));     // NEW: name resolved from barcode
        Assert.Equal("5011921156993", LS("itemIdOne"));   // NEW: barcode present
        Assert.Equal("2079", LS("unitPricePence"));        // existing portal field intact

        var adjustments = ((System.Collections.IEnumerable)body.GetType().GetProperty("adjustments")!.GetValue(body)!).Cast<object>().ToList();
        Assert.Single(adjustments);
        var a0 = adjustments[0];
        Assert.Equal("Refund", a0.GetType().GetProperty("type")!.GetValue(a0)!.ToString());
        Assert.Equal("500", a0.GetType().GetProperty("amountPence")!.GetValue(a0)!.ToString());
    }
}
