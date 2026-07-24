using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>WP1.3 sales v2: constructor-enforced money invariants + a property-based round-trip
/// (1000 random valid sales survive EF persistence with invariants intact).</summary>
public class SalesV2Tests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(Tenant)) { CurrentUser = "sales-test" };

    // Build a valid sale from a seeded RNG: lines reconcile to gross, VAT = Σ line VAT,
    // one tender nets to gross (optionally with change).
    private static SaleV2 RandomValidSale(Random rng)
    {
        var saleId = Guid.NewGuid();
        var lineCount = rng.Next(1, 4);
        var lines = new List<SaleLine>();
        for (var i = 0; i < lineCount; i++)
        {
            long unit = rng.Next(1, 10_000);
            int qty = rng.Next(1, 6);
            long maxDisc = unit * qty / 2;
            long disc = maxDisc == 0 ? 0 : rng.Next(0, (int)maxDisc + 1);
            long lineGross = unit * qty - disc;
            int vatBp = 2000;
            long vat = lineGross * vatBp / 10_000;
            lines.Add(new SaleLine
            {
                Id = Guid.NewGuid(), TenantId = Tenant, SaleId = saleId, LineNo = i + 1,
                ItemId = Guid.NewGuid(), Qty = qty, UnitPricePence = unit, DiscountPence = disc,
                LineGrossPence = lineGross, VatRateBp = vatBp, VatAmountPence = vat,
            });
        }
        long gross = lines.Sum(l => l.LineGrossPence);
        long vatTotal = lines.Sum(l => l.VatAmountPence);
        long change = rng.Next(0, 2) == 1 ? rng.Next(1, 500) : 0;
        var tenders = new List<SaleTender>
        {
            new() { Id = Guid.NewGuid(), TenantId = Tenant, SaleId = saleId,
                    TenderType = TenderType.Cash, AmountPence = gross + change, ChangePence = change },
        };
        return SaleV2.Create(saleId, Tenant, Guid.NewGuid(), Guid.NewGuid(), rng.Next(),
            SaleChannel.Till, new DateOnly(2026, 7, 24), DateTime.UtcNow, DateTime.UtcNow,
            gross, vatTotal, lines, tenders);
    }

    [Fact]
    public void Inconsistent_sales_throw()
    {
        var line = new SaleLine { LineNo = 1, Qty = 2, UnitPricePence = 100, DiscountPence = 0, LineGrossPence = 200, VatAmountPence = 40 };
        var okTender = new SaleTender { AmountPence = 200, ChangePence = 0 };

        // Gross != Σ line gross.
        Assert.Throws<InvalidSaleException>(() => SaleV2.Create(
            Guid.NewGuid(), Tenant, Guid.NewGuid(), Guid.NewGuid(), 1, SaleChannel.Till,
            default, DateTime.UtcNow, DateTime.UtcNow, 999, 40, new[] { line }, new[] { okTender }));

        // Line gross != unit×qty − discount.
        var badLine = new SaleLine { LineNo = 1, Qty = 2, UnitPricePence = 100, DiscountPence = 0, LineGrossPence = 150, VatAmountPence = 30 };
        Assert.Throws<InvalidSaleException>(() => SaleV2.Create(
            Guid.NewGuid(), Tenant, Guid.NewGuid(), Guid.NewGuid(), 1, SaleChannel.Till,
            default, DateTime.UtcNow, DateTime.UtcNow, 150, 30, new[] { badLine }, new[] { new SaleTender { AmountPence = 150 } }));

        // VAT total mismatch.
        Assert.Throws<InvalidSaleException>(() => SaleV2.Create(
            Guid.NewGuid(), Tenant, Guid.NewGuid(), Guid.NewGuid(), 1, SaleChannel.Till,
            default, DateTime.UtcNow, DateTime.UtcNow, 200, 999, new[] { line }, new[] { okTender }));

        // Tender net != gross.
        Assert.Throws<InvalidSaleException>(() => SaleV2.Create(
            Guid.NewGuid(), Tenant, Guid.NewGuid(), Guid.NewGuid(), 1, SaleChannel.Till,
            default, DateTime.UtcNow, DateTime.UtcNow, 200, 40, new[] { line }, new[] { new SaleTender { AmountPence = 175 } }));
    }

    [Fact]
    public void Thousand_random_valid_sales_round_trip_with_invariants_intact()
    {
        var rng = new Random(1337); // deterministic
        var sales = Enumerable.Range(0, 1000).Select(_ => RandomValidSale(rng)).ToList();

        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using (var ctx = Ctx(conn))
        {
            ctx.Database.EnsureCreated();
            ctx.SalesV2.AddRange(sales);
            ctx.SaveChanges();
        }

        using (var ctx = Ctx(conn))
        {
            var reloaded = ctx.SalesV2.Include(s => s.Lines).Include(s => s.Tenders).ToList();
            Assert.Equal(1000, reloaded.Count);
            foreach (var s in reloaded)
                s.Validate(); // throws if any invariant broke through persistence
            // spot-check totals survived to the penny
            Assert.Equal(sales.Sum(s => s.GrossPence), reloaded.Sum(s => s.GrossPence));
            Assert.Equal(sales.Sum(s => s.VatPence), reloaded.Sum(s => s.VatPence));
        }
    }
}
