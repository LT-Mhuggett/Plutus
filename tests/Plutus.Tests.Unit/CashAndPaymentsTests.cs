using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Cash;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.Payments;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP7 (DoD): the Z-report day matches the recorded tenders/rollup source to the penny;
/// one Z per till per business day; cash-event ingest is idempotent; an ORPHANED PAYMENT
/// (capture confirmed, sale POST dropped) surfaces in the unresolved queue and resolves
/// when the sale finally lands.
/// </summary>
public class CashAndPaymentsTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid TillId = Guid.NewGuid();
    private static readonly Guid DeviceId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 7, 25);

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(Tenant)) { CurrentUser = "cash-test" };

    private static SqliteConnection OpenSeeded()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Ctx(conn);
        ctx.Database.EnsureCreated();
        ctx.Devices.Add(new Device
        {
            Id = DeviceId, TenantId = Tenant, TillId = TillId,
            SecretHash = new byte[64], SecretSalt = new byte[32],
            Status = DeviceStatus.Active, CreatedAtUtc = DateTime.UtcNow,
        });
        ctx.SaveChanges();
        return conn;
    }

    private static long _seq;

    /// <summary>A recorded sale on the till: cash tender net of change (+ optional card part).</summary>
    private static void Sale(MySqlDbContext ctx, long cashNet, long cardNet = 0, string providerRef = null)
    {
        var saleId = Uuid7.New();
        var seq = System.Threading.Interlocked.Increment(ref _seq);
        long gross = cashNet + cardNet;
        long vat = 0;
        var line = new SaleLine
        {
            Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId, LineNo = 1,
            ItemId = Guid.NewGuid(), Qty = 1, UnitPricePence = gross, DiscountPence = 0,
            LineGrossPence = gross, VatRateBp = 0, VatAmountPence = 0,
        };
        var tenders = new List<SaleTender>
        {
            new() { Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId, TenderType = TenderType.Cash, AmountPence = cashNet + 100, ChangePence = 100 },
        };
        if (cardNet > 0)
            tenders.Add(new SaleTender
            {
                Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId, TenderType = TenderType.Card,
                AmountPence = cardNet, ChangePence = 0, ProviderRef = providerRef,
            });
        ctx.SalesV2.Add(SaleV2.Create(saleId, Tenant, TillId, DeviceId, seq, SaleChannel.WebPos,
            Day, DateTime.UtcNow, DateTime.UtcNow, gross, vat, new[] { line }, tenders));
    }

    private static CashEventRequest Req(string type, long amount = 0, long? counted = null, string reason = null) => new()
    {
        EventId = Uuid7.New(), DeviceId = DeviceId, Type = type, BusinessDay = Day,
        OccurredAtUtc = DateTime.UtcNow, AmountPence = amount, CountedPence = counted, Reason = reason,
    };

    [Fact]
    public async Task Z_close_computes_expected_from_float_takings_ins_outs_to_the_penny()
    {
        using var conn = OpenSeeded();
        using (var ctx = Ctx(conn))
        {
            Sale(ctx, cashNet: 1500);
            Sale(ctx, cashNet: 2245);
            ctx.SaveChanges();
        }

        using var db = Ctx(conn);
        var service = new CashEventService(db);

        Assert.Equal(201, (await service.IngestAsync(Req("OpenFloat", 5000), Tenant, DeviceId, "t")).Status);
        Assert.Equal(201, (await service.IngestAsync(Req("PaidIn", 700, reason: "till top-up"), Tenant, DeviceId, "t")).Status);
        Assert.Equal(201, (await service.IngestAsync(Req("PaidOut", 250, reason: "window cleaner"), Tenant, DeviceId, "t")).Status);

        // X snapshot first (non-closing)
        var x = await service.IngestAsync(Req("XSnapshot", counted: 9195), Tenant, DeviceId, "t");
        Assert.Equal(201, x.Status);

        // expected = 5000 + (1500+2245) + 700 − 250 = 9195 → counted 9200 = +5 variance
        var z = await service.IngestAsync(Req("ZClose", counted: 9200), Tenant, DeviceId, "t");
        Assert.Equal(201, z.Status);

        var zRow = await db.CashEvents.SingleAsync(e => e.Type == CashEventType.ZClose);
        Assert.Equal(9195, zRow.ExpectedPence);
        Assert.Equal(5, zRow.VariancePence);
        var xRow = await db.CashEvents.SingleAsync(e => e.Type == CashEventType.XSnapshot);
        Assert.Equal(9195, xRow.ExpectedPence);
        Assert.Equal(0, xRow.VariancePence);
    }

    [Fact]
    public async Task One_Z_per_business_day_and_ingest_is_idempotent()
    {
        using var conn = OpenSeeded();
        using var db = Ctx(conn);
        var service = new CashEventService(db);

        var z1 = Req("ZClose", counted: 0);
        Assert.Equal(201, (await service.IngestAsync(z1, Tenant, DeviceId, "t")).Status);

        // replaying the SAME event id → 200, no new row
        Assert.Equal(200, (await service.IngestAsync(z1, Tenant, DeviceId, "t")).Status);
        Assert.Equal(1, await db.CashEvents.CountAsync());

        // a SECOND Z (new id) for the same till+day → 409
        Assert.Equal(409, (await service.IngestAsync(Req("ZClose", counted: 0), Tenant, DeviceId, "t")).Status);
        // and any post-Z event for the closed day → 409
        Assert.Equal(409, (await service.IngestAsync(Req("PaidIn", 100, reason: "late"), Tenant, DeviceId, "t")).Status);
    }

    [Fact]
    public async Task Orphaned_payment_surfaces_in_the_queue_and_resolves_when_the_sale_lands()
    {
        using var conn = OpenSeeded();
        const string terminalRef = "TERM-4711-CAP-001";

        // capture succeeded at the terminal… but the sale POST was dropped
        using (var db = Ctx(conn))
        {
            db.PaymentEvents.Add(new PaymentEvent
            {
                Id = Uuid7.New(), TenantId = Tenant, Provider = "fake", ProviderRef = terminalRef,
                AmountPence = 2599, Status = PaymentEventStatus.Captured,
                CapturedAtUtc = DateTime.UtcNow, ReceivedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using (var db = Ctx(conn))
        {
            var (matched, unresolved) = await new PaymentReconciliationService(db).ReconcileAsync();
            Assert.Equal(0, matched);
            Assert.Equal(1, unresolved); // the orphan is visible
        }

        // the till reconnects and drains its outbox — the sale arrives with the terminal ref
        using (var ctx = Ctx(conn))
        {
            Sale(ctx, cashNet: 0 + 1, cardNet: 2599, providerRef: terminalRef); // 1p cash to keep tender net == gross shape
            ctx.SaveChanges();
        }

        using (var db = Ctx(conn))
        {
            var (matched, unresolved) = await new PaymentReconciliationService(db).ReconcileAsync();
            Assert.Equal(1, matched);
            Assert.Equal(0, unresolved);
            var evt = await db.PaymentEvents.SingleAsync();
            Assert.NotNull(evt.MatchedSaleId);
            Assert.NotNull(evt.ResolvedAtUtc);
        }
    }
}
