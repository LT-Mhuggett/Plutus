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

    /// <summary>
    /// A REFUND-ONLY sale (everything returned, nothing bought) must satisfy the same four
    /// invariants with every figure negative: negative qty → negative line gross → negative
    /// gross → a negative net tender, i.e. money paid OUT. Pinning this because the till used to
    /// refuse such a basket ("Refund-only baskets aren't supported yet"), and the reason to
    /// believe that block is safe to lift is precisely that nothing here forbids the signs.
    /// </summary>
    [Fact]
    public void A_refund_only_sale_satisfies_the_invariants()
    {
        var saleId = Guid.NewGuid();
        // returning 2 × £14.58 (inc 20% VAT) = −£29.16
        const long unit = 1458;
        const int qty = -2;
        const long lineGross = unit * qty;                 // −2916
        const long vat = lineGross * 2000 / 10_000;        // −583
        var lines = new List<SaleLine>
        {
            new() { Id = Guid.NewGuid(), TenantId = Tenant, SaleId = saleId, LineNo = 1,
                    ItemId = Guid.NewGuid(), Qty = qty, UnitPricePence = unit, DiscountPence = 0,
                    LineGrossPence = lineGross, VatRateBp = 2000, VatAmountPence = vat },
        };
        var tenders = new List<SaleTender>
        {
            // cash OUT of the drawer: a negative amount, no change
            new() { Id = Guid.NewGuid(), TenantId = Tenant, SaleId = saleId,
                    TenderType = TenderType.Cash, AmountPence = lineGross, ChangePence = 0 },
        };

        var sale = SaleV2.Create(saleId, Tenant, Guid.NewGuid(), Guid.NewGuid(), 1,
            SaleChannel.Till, new DateOnly(2026, 8, 7), DateTime.UtcNow, DateTime.UtcNow,
            lineGross, vat, lines, tenders);

        sale.Validate();                       // throws if any invariant breaks
        Assert.Equal(-2916, sale.GrossPence);
        Assert.Equal(-583, sale.VatPence);
    }

    /// <summary>A refund still has to reconcile: paying out the wrong amount must be refused,
    /// so lifting the UI block cannot let an unbalanced refund through.</summary>
    [Fact]
    public void A_refund_whose_tender_does_not_match_is_rejected()
    {
        var saleId = Guid.NewGuid();
        var lines = new List<SaleLine>
        {
            new() { Id = Guid.NewGuid(), TenantId = Tenant, SaleId = saleId, LineNo = 1,
                    ItemId = Guid.NewGuid(), Qty = -1, UnitPricePence = 1000, DiscountPence = 0,
                    LineGrossPence = -1000, VatRateBp = 2000, VatAmountPence = -200 },
        };
        var tenders = new List<SaleTender>
        {
            new() { Id = Guid.NewGuid(), TenantId = Tenant, SaleId = saleId,
                    TenderType = TenderType.Cash, AmountPence = -900, ChangePence = 0 }, // short by £1
        };

        Assert.Throws<InvalidSaleException>(() => SaleV2.Create(
            saleId, Tenant, Guid.NewGuid(), Guid.NewGuid(), 1, SaleChannel.Till,
            new DateOnly(2026, 8, 7), DateTime.UtcNow, DateTime.UtcNow, -1000, -200, lines, tenders));
    }

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
