using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.Sales;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP1.7 standing suites (permanent). Money reconciliation: a random batch of sales ingested
/// sums, in the DB, to exactly what was submitted (to the penny). Idempotency replay: replaying
/// the whole batch twice changes no row counts. (The route-level tenant-isolation suite needs
/// the HTTP host harness — tracked separately; row-level isolation itself is covered by
/// TenancyTests.)
/// </summary>
public class StandingSuitesTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid DeviceId = Guid.NewGuid();

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(Tenant)) { CurrentUser = "standing-test" };

    private static SqliteConnection OpenWithDevice()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var ctx = Ctx(conn);
        ctx.Database.EnsureCreated();
        ctx.Devices.Add(new Device
        {
            Id = DeviceId, TenantId = Tenant, TillId = Guid.NewGuid(),
            SecretHash = new byte[64], SecretSalt = new byte[32],
            Status = DeviceStatus.Active, CreatedAtUtc = DateTime.UtcNow,
        });
        ctx.SaveChanges();
        return conn;
    }

    private static IngestSaleRequest RandomValidSale(Random rng, long seq)
    {
        var saleId = Guid.NewGuid();
        var lines = new List<IngestLine>();
        int n = rng.Next(1, 5);
        for (var i = 0; i < n; i++)
        {
            long unit = rng.Next(1, 5000);
            int qty = rng.Next(1, 4);
            long disc = rng.Next(0, (int)(unit * qty / 2) + 1);
            long gross = unit * qty - disc;
            lines.Add(new IngestLine
            {
                ItemId = Guid.NewGuid(), Qty = qty, UnitPricePence = unit, DiscountPence = disc,
                LineGrossPence = gross, VatRateBp = 2000, VatAmountPence = gross * 2000 / 10000,
            });
        }
        long grossTotal = lines.Sum(l => l.LineGrossPence);
        long vatTotal = lines.Sum(l => l.VatAmountPence);
        return new IngestSaleRequest
        {
            SaleId = saleId, DeviceId = DeviceId, DeviceSeq = seq, Channel = 0,
            BusinessDay = new DateOnly(2026, 7, 24), OccurredAtUtc = DateTime.UtcNow,
            GrossPence = grossTotal, VatPence = vatTotal,
            Lines = lines,
            Tenders = new List<IngestTender> { new() { TenderType = 0, AmountPence = grossTotal, ChangePence = 0 } },
        };
    }

    private static async Task IngestAll(SqliteConnection conn, IEnumerable<IngestSaleRequest> batch)
    {
        foreach (var req in batch)
        {
            using var ctx = Ctx(conn);
            await new SalesIngestService(ctx).IngestAsync(req, Tenant, DeviceId, "op");
        }
    }

    [Fact]
    public async Task Money_reconciliation_db_totals_equal_submitted_to_the_penny()
    {
        var rng = new Random(4242);
        var batch = Enumerable.Range(0, 300).Select(i => RandomValidSale(rng, i + 1)).ToList();
        long submittedGross = batch.Sum(b => b.GrossPence);
        long submittedVat = batch.Sum(b => b.VatPence);

        using var conn = OpenWithDevice();
        await IngestAll(conn, batch);

        using var ctx = Ctx(conn);
        Assert.Equal(batch.Count, await ctx.SalesV2.CountAsync());
        Assert.Equal(submittedGross, await ctx.SalesV2.SumAsync(s => s.GrossPence));
        Assert.Equal(submittedVat, await ctx.SalesV2.SumAsync(s => s.VatPence));
        // line-level cross-check
        Assert.Equal(submittedGross, await ctx.SaleLines.SumAsync(l => l.LineGrossPence));
        Assert.Equal(submittedVat, await ctx.SaleLines.SumAsync(l => l.VatAmountPence));
    }

    [Fact]
    public async Task Idempotency_replaying_the_whole_batch_twice_changes_no_row_counts()
    {
        var rng = new Random(99);
        var batch = Enumerable.Range(0, 100).Select(i => RandomValidSale(rng, i + 1)).ToList();

        using var conn = OpenWithDevice();
        await IngestAll(conn, batch);

        int sales1, lines1, tenders1, outbox1;
        using (var ctx = Ctx(conn))
        {
            sales1 = await ctx.SalesV2.CountAsync();
            lines1 = await ctx.SaleLines.CountAsync();
            tenders1 = await ctx.SaleTenders.CountAsync();
            outbox1 = await ctx.OutboxEvents.CountAsync();
        }
        Assert.Equal(batch.Count, sales1);

        // Replay the identical log twice.
        await IngestAll(conn, batch);
        await IngestAll(conn, batch);

        using (var ctx = Ctx(conn))
        {
            Assert.Equal(sales1, await ctx.SalesV2.CountAsync());
            Assert.Equal(lines1, await ctx.SaleLines.CountAsync());
            Assert.Equal(tenders1, await ctx.SaleTenders.CountAsync());
            Assert.Equal(outbox1, await ctx.OutboxEvents.CountAsync());
        }
    }
}
