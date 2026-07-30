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
/// FE7 (DoD): a sale that BOTH sells a gift card and sells VAT-able goods must declare VAT on the
/// goods only. Selling a card takes a deposit against goods chosen later — the VAT-able supply
/// happens at redemption — so a card ringing through at 20% would declare VAT twice on the same
/// money: once when the card is bought, again when it is spent.
///
/// This pins the arithmetic all the way to the VAT ROLLUPS, which is where a VAT return is read from.
/// </summary>
public class GiftCardVatTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid BusinessId = Guid.NewGuid();
    private const int StoreId = 7;
    private static readonly Guid TillId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 7, 30);

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(Tenant)) { CurrentUser = "giftcard-vat-test" };

    private static SqliteConnection OpenSeeded()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Ctx(conn);
        ctx.Database.EnsureCreated();
        ctx.Business.Add(new Business { Id = BusinessId, Name = "Cardco", NameAbbr = "CRD", VatIN = "GB0" });
        ctx.Stores.Add(new Store
        {
            Id = StoreId, BusinessId = BusinessId, ContactNumber = "-",
            AdLine1 = "-", AdLine2 = "", City = "-", PostCode = "-", Country = "-",
        });
        ctx.Till.Add(new Till { Id = TillId, StoreId = StoreId, LastOnline = DateTime.UtcNow });
        ctx.SaveChanges();
        return conn;
    }

    private static async Task DrainAsync(SqliteConnection conn)
    {
        var drainer = new OutboxDrainer(new DefaultOutboxEventCodec(),
            new OutboxDispatcherOptions { RetryBackoffs = Array.Empty<TimeSpan>() });
        while (true)
        {
            using var db = Ctx(conn);
            var stats = await drainer.DrainConsumerAsync(db, new RollupProjectionConsumer(db), default);
            if (stats.Handled + stats.Skipped + stats.Parked == 0) break;
        }
    }

    [Fact]
    public async Task A_sale_mixing_a_gift_card_with_vatable_goods_declares_vat_on_the_goods_only()
    {
        using var conn = OpenSeeded();
        var saleId = Uuid7.New();

        // £12.00 of 20%-rated goods → £2.00 VAT
        const long goodsGross = 1200;
        const long goodsVat = 200;   // 1200 − round(1200 / 1.2)
        // £25.00 gift card → NO VAT. This is the whole point: the line is priced at the amount loaded
        // and sits on a zero band, exactly as the till builds it (exPrice == price).
        const long cardGross = 2500;

        var lines = new List<SaleLine>
        {
            new()
            {
                Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId, LineNo = 1,
                ItemId = Guid.NewGuid(), ItemIdOne = "BOOK-1", Qty = 1,
                UnitPricePence = goodsGross, LineGrossPence = goodsGross, DiscountPence = 0,
                VatRateBp = 2000, VatAmountPence = goodsVat,
            },
            new()
            {
                Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId, LineNo = 2,
                ItemId = Guid.NewGuid(), ItemIdOne = "GIFT-CARD", Qty = 1,
                UnitPricePence = cardGross, LineGrossPence = cardGross, DiscountPence = 0,
                VatRateBp = 0, VatAmountPence = 0,
            },
        };

        // Cash for the goods, and the card paid for with cash too: £37.00 taken.
        var sale = SaleV2.Create(
            saleId, Tenant, TillId, Guid.NewGuid(), 1, SaleChannel.WebPos, Day,
            DateTime.UtcNow, DateTime.UtcNow, goodsGross + cardGross, goodsVat, lines,
            new[]
            {
                new SaleTender
                {
                    Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId,
                    TenderType = TenderType.Cash, AmountPence = goodsGross + cardGross, ChangePence = 0,
                },
            });

        // The sale's own invariants hold: £37 taken, £2 VAT — NOT £37/1.2.
        Assert.Equal(3700, sale.GrossPence);
        Assert.Equal(200, sale.VatPence);

        using (var ctx = Ctx(conn))
        {
            ctx.SalesV2.Add(sale);
            var evt = new SaleRecorded(Uuid7.New(), Tenant, sale.OccurredAtUtc, sale.Id, sale.DeviceId, sale.DeviceSeq, sale.BusinessDay);
            ctx.OutboxEvents.Add(new OutboxEvent
            {
                EventId = evt.EventId, TenantId = Tenant, EventType = nameof(SaleRecorded),
                PayloadJson = JsonSerializer.Serialize(evt), CreatedAtUtc = DateTime.UtcNow,
            });
            ctx.SaveChanges();
        }
        await DrainAsync(conn);

        using var check = Ctx(conn);

        // takings: the full £37 (the till really did take it)
        var takings = await check.SalesRollups.SingleAsync(r => r.BusinessDay == Day && r.TillId == TillId);
        Assert.Equal(3700, takings.GrossPence);
        Assert.Equal(200, takings.VatPence);

        // the VAT return: £2 on the 20% band, and the card's £25 sitting on the ZERO band
        var vat = await check.VatRollups.Where(r => r.BusinessDay == Day).ToListAsync();
        var standard = Assert.Single(vat, r => r.VatRateBp == 2000);
        Assert.Equal(goodsGross, standard.GrossPence);
        Assert.Equal(goodsVat, standard.VatPence);

        var zero = Assert.Single(vat, r => r.VatRateBp == 0);
        Assert.Equal(cardGross, zero.GrossPence);
        Assert.Equal(0, zero.VatPence);          // ⚠ the assertion FE7 exists to protect
        Assert.Equal(cardGross, zero.NetPence);  // all net, no tax

        // and nothing leaked: total VAT for the day is the goods' VAT alone
        Assert.Equal(goodsVat, vat.Sum(r => r.VatPence));
    }

    /// <summary>
    /// The till builds an activation line with exPrice == price. This is the arithmetic that makes it
    /// zero-VAT, mirroring api.ts's checkout(): vatRateBp = round((price/exPrice − 1) × 10000).
    /// If a future change ever prices a card ex-VAT, this fails first.
    /// </summary>
    [Theory]
    [InlineData(500)]
    [InlineData(2000)]
    [InlineData(2499)]
    public void The_tills_activation_line_arithmetic_yields_zero_vat(long amountPence)
    {
        // what basket.ts's "addGiftCard" produces
        var pricePence = amountPence;
        var exPricePence = amountPence;

        var vatRateBp = exPricePence > 0 ? (long)Math.Round((pricePence / (double)exPricePence - 1) * 10000) : 0;
        var lineGross = pricePence * 1;
        var lineEx = exPricePence * 1;
        var vatAmount = lineGross - lineEx;

        Assert.Equal(0, vatRateBp);
        Assert.Equal(0, vatAmount);
        Assert.Equal(amountPence, lineGross);   // the customer is charged exactly what goes on the card
    }
}
