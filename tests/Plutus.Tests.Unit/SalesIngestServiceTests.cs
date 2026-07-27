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

/// <summary>WP1.4 idempotent ingest: record → 201 (+ outbox + LastSeenSeq bump), duplicate → 200,
/// invariant failure → quarantine 202 (idempotent), and monotonic device sequence.</summary>
public class SalesIngestServiceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid DeviceId = Guid.NewGuid();
    private static readonly Guid TillId = Guid.NewGuid();

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(Tenant)) { CurrentUser = "ingest-test" };

    private static SqliteConnection OpenWithDevice()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var ctx = Ctx(conn);
        ctx.Database.EnsureCreated();
        ctx.Devices.Add(new Device
        {
            Id = DeviceId, TenantId = Tenant, TillId = TillId,
            SecretHash = new byte[64], SecretSalt = new byte[32],
            Status = DeviceStatus.Active, LastSeenSeq = 0, CreatedAtUtc = DateTime.UtcNow,
        });
        ctx.SaveChanges();
        return conn;
    }

    private static IngestSaleRequest ValidReq(Guid saleId, long seq)
    {
        return new IngestSaleRequest
        {
            SaleId = saleId, DeviceId = DeviceId, DeviceSeq = seq, Channel = 0,
            BusinessDay = new DateOnly(2026, 7, 24), OccurredAtUtc = DateTime.UtcNow,
            GrossPence = 200, VatPence = 40,
            Lines = new List<IngestLine>
            {
                new() { ItemId = Guid.NewGuid(), Qty = 2, UnitPricePence = 100, DiscountPence = 0,
                        LineGrossPence = 200, VatRateBp = 2000, VatAmountPence = 40 },
            },
            Tenders = new List<IngestTender>
            {
                new() { TenderType = 0, AmountPence = 200, ChangePence = 0 },
            },
        };
    }

    private static async Task<IngestOutcome> Ingest(SqliteConnection conn, IngestSaleRequest req)
    {
        using var ctx = Ctx(conn);
        return await new SalesIngestService(ctx).IngestAsync(req, Tenant, DeviceId, "op");
    }

    [Fact]
    public async Task Records_sale_writes_outbox_and_bumps_lastseenseq()
    {
        using var conn = OpenWithDevice();
        var saleId = Guid.NewGuid();

        var outcome = await Ingest(conn, ValidReq(saleId, 5));
        Assert.Equal(201, outcome.Status);

        using var ctx = Ctx(conn);
        Assert.True(await ctx.SalesV2.AnyAsync(s => s.Id == saleId));
        Assert.Equal(1, await ctx.OutboxEvents.CountAsync(e => e.EventType == "SaleRecorded"));
        Assert.Equal(5, (await ctx.Devices.FirstAsync(d => d.Id == DeviceId)).LastSeenSeq);
    }

    [Fact]
    public async Task Duplicate_saleId_returns_200_and_keeps_one_row_and_one_event()
    {
        using var conn = OpenWithDevice();
        var saleId = Guid.NewGuid();

        var first = await Ingest(conn, ValidReq(saleId, 1));
        var second = await Ingest(conn, ValidReq(saleId, 1));

        Assert.Equal(201, first.Status);
        Assert.Equal(200, second.Status);

        using var ctx = Ctx(conn);
        Assert.Equal(1, await ctx.SalesV2.CountAsync(s => s.Id == saleId));
        Assert.Equal(1, await ctx.OutboxEvents.CountAsync()); // no second event
    }

    [Fact]
    public async Task Invariant_failure_quarantines_202_and_is_idempotent()
    {
        using var conn = OpenWithDevice();
        var saleId = Guid.NewGuid();
        var bad = ValidReq(saleId, 1);
        bad.GrossPence = 999; // != Σ line gross (200)

        var first = await Ingest(conn, bad);
        var second = await Ingest(conn, bad);

        Assert.Equal(202, first.Status);
        Assert.Equal(202, second.Status);

        using var ctx = Ctx(conn);
        Assert.Equal(0, await ctx.SalesV2.CountAsync(s => s.Id == saleId));
        Assert.Equal(1, await ctx.SaleQuarantine.CountAsync(q => q.SaleId == saleId)); // one row
    }

    [Fact]
    public async Task Device_sequence_is_monotonic()
    {
        using var conn = OpenWithDevice();
        await Ingest(conn, ValidReq(Guid.NewGuid(), 5));
        await Ingest(conn, ValidReq(Guid.NewGuid(), 3)); // lower — must not lower LastSeenSeq
        using (var ctx = Ctx(conn))
            Assert.Equal(5, (await ctx.Devices.FirstAsync(d => d.Id == DeviceId)).LastSeenSeq);

        await Ingest(conn, ValidReq(Guid.NewGuid(), 10)); // higher — advances
        using (var ctx = Ctx(conn))
            Assert.Equal(10, (await ctx.Devices.FirstAsync(d => d.Id == DeviceId)).LastSeenSeq);
    }
}
