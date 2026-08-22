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
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// **Re-submitting a sale, and the one case where the shortcut must NOT fire.**
///
/// ⚠ WHY THE SHORTCUT EXISTS: the ingest was answering a duplicate by ATTEMPTING the insert, taking
/// the primary-key violation, rolling back and re-reading. Correct, and expensive — and for the
/// webstore it is not an edge case but every cycle, because the reconciler deliberately re-reads an
/// overlap window so an edited order is picked up. One Kapow order produced 94 duplicate-key
/// failures in a single day, every one of them logged as an error with a stack trace.
///
/// ⚠⚠ AND WHY IT CHECKS `SalesV2` RATHER THAN `ReadExistingOutcomeAsync`, WHICH WAS THE FIRST
/// ATTEMPT AND WAS WRONG. That helper also answers 202 for a QUARANTINED sale — and a quarantined
/// sale is exactly the one whose answer may have changed since. `POST /webstores/{id}/retry`
/// re-submits parked sales *because* an operator has since bound the missing SKU or fixed the band,
/// and it is `IngestAsync` re-running validation that turns one into a recorded sale. Worse, that
/// endpoint stamps `ResolvedAtUtc` on the reply it gets back — so short-circuiting would have marked
/// the quarantine healed while the sale was never ingested at all. The second test is that case.
/// </summary>
public class IngestIdempotencyTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Device = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 8, 22);
    private static readonly DateTime At = new(2026, 8, 22, 10, 0, 0, DateTimeKind.Utc);

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
               new FixedTenantContext(Tenant)) { CurrentUser = "ingest-test" };

    private static SqliteConnection Open()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Ctx(conn);
        ctx.Database.EnsureCreated();
        return conn;
    }

    /// <summary>A sale whose line/tender arithmetic balances, so nothing quarantines it.</summary>
    private static IngestSaleRequest Req(
        Guid saleId, long gross = 1200, string discountsJson = null, long vat = 0, int vatRateBp = 0) =>
        new()
        {
            SaleId = saleId, DeviceId = Device, DeviceSeq = 1, Channel = (byte)SaleChannel.WebPos,
            BusinessDay = Day, OccurredAtUtc = At, GrossPence = gross, VatPence = vat,
            Lines = new List<IngestLine>
            {
                new()
                {
                    ItemId = Guid.NewGuid(), Qty = 1, UnitPricePence = gross, DiscountPence = 0,
                    LineGrossPence = gross, VatRateBp = vatRateBp, VatAmountPence = vat, DiscountsJson = discountsJson,
                },
            },
            Tenders = new List<IngestTender>
            {
                new() { TenderType = (byte)TenderType.Cash, AmountPence = gross, ChangePence = 0 },
            },
        };

    [Fact]
    public async Task A_resubmitted_sale_answers_200_and_is_stored_once()
    {
        using var conn = Open();
        var saleId = Uuid7.New();

        using (var db = Ctx(conn))
        {
            var first = await new SalesIngestService(db).IngestAsync(Req(saleId), Tenant, Device, "till");
            Assert.Equal(201, first.Status);
        }

        using (var db = Ctx(conn))
        {
            // ⚠ The webstore's overlap window, in one line: the same order, submitted again.
            var again = await new SalesIngestService(db).IngestAsync(Req(saleId), Tenant, Device, "webstore:1");
            Assert.Equal(200, again.Status);
        }

        using (var db = Ctx(conn))
            Assert.Equal(1, await db.SalesV2.IgnoreQueryFilters().CountAsync(s => s.Id == saleId));
    }

    /// <summary>
    /// ⚠⚠ THE REGRESSION GUARD. A sale parked in quarantine, then re-submitted once the reason is
    /// gone, must RECORD — not be waved through as "already dealt with".
    /// </summary>
    [Fact]
    public async Task A_quarantined_sale_still_records_when_resubmitted_after_the_problem_is_fixed()
    {
        using var conn = Open();
        var saleId = Uuid7.New();

        // ⚠ StaleBand needs a rate the tenant KNEW AT ANOTHER TIME and no longer has in force —
        // that is the whole point of the check (a till offline across a rate change). One band is
        // not enough: an unexplained rate with no history behind it is OffBand, which deliberately
        // never blocks trading.
        using (var db = Ctx(conn))
        {
            db.VatRatePoints.Add(new VatRatePoint
            {
                Id = Uuid7.New(), TenantId = Tenant, Band = "standard", RateBp = 1500,
                EffectiveFromUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            db.VatRatePoints.Add(new VatRatePoint
            {
                Id = Uuid7.New(), TenantId = Tenant, Band = "standard", RateBp = 2000,
                EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            await db.SaveChangesAsync();
        }

        // 1150 inc / 1000 ex is 15% — the SUPERSEDED band. Priced today, that is a stale till.
        var stale = "{\"exUnitPence\":1000}";

        using (var db = Ctx(conn))
        {
            var parked = await new SalesIngestService(db)
                .IngestAsync(Req(saleId, 1150, stale, vat: 150, vatRateBp: 1500), Tenant, Device, "till");
            Assert.Equal(202, parked.Status);
        }

        using (var db = Ctx(conn))
            Assert.True(await db.SaleQuarantine.IgnoreQueryFilters().AnyAsync(q => q.SaleId == saleId),
                "the sale should be parked — if it is not, this test is no longer exercising the heal.");

        // The reason is resolved (here: the band history is corrected away). This is what
        // `POST /webstores/{id}/retry` does next — re-submit the SAME saleId.
        using (var db = Ctx(conn))
        {
            db.VatRatePoints.RemoveRange(await db.VatRatePoints.IgnoreQueryFilters().ToListAsync());
            await db.SaveChangesAsync();
        }

        using (var db = Ctx(conn))
        {
            var healed = await new SalesIngestService(db).IngestAsync(Req(saleId, 1150, stale, vat: 150, vatRateBp: 1500), Tenant, Device, "webstore:1");
            Assert.Equal(201, healed.Status);
        }

        using (var db = Ctx(conn))
            Assert.Equal(1, await db.SalesV2.IgnoreQueryFilters().CountAsync(s => s.Id == saleId));
    }
}
