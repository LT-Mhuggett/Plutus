using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Client.Core;
using Plutus.Client.Storage;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// Cutover step 15 — reading a committed sale back.
///
/// ⚠ NOTHING COULD DO THIS. `GetPendingAsync` filters to Pending, so the instant a sale was
/// delivered it became unreadable by the till that sold it: no reprint, no receipt-led refund, no
/// offline X/Z. The row was in the table the whole time.
/// </summary>
public class TillStoreReadPathTests : IAsyncLifetime
{
    private SqliteConnection _conn = null!;
    private TillDbContext _db = null!;
    private TillStore _store = null!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        await _conn.OpenAsync();
        _db = new TillDbContext(new DbContextOptionsBuilder<TillDbContext>().UseSqlite(_conn).Options);
        await _db.EnsureReadyAsync();
        _store = new TillStore(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    /// <summary>A sale line that gives goods back to <paramref name="origin"/>.</summary>
    private static IngestLine ReturnLine(Guid origin, long grossPence) => new()
    {
        Qty = -1,
        UnitPricePence = Math.Abs(grossPence),
        // ⚠ NEGATIVE, as a return really is. The store must take a magnitude from this.
        LineGrossPence = -Math.Abs(grossPence),
        DiscountsJson = new LineMeta
        {
            ItemIdOne = "5010001",
            Return = new ReturnRef { OriginSaleId = origin.ToString("D") },
        }.ToJson(),
    };

    private static IngestSaleRequest Sale(params IngestLine[] lines)
    {
        var sale = new IngestSaleRequest
        {
            SaleId = Uuid7.New(),
            BusinessDay = new DateOnly(2026, 8, 9),
            OccurredAtUtc = DateTime.UtcNow,
            GrossPence = 600,
            VatPence = 100,
            Tenders = { new IngestTender { TenderType = Tenders.Cash, AmountPence = 600 } },
        };

        if (lines.Length == 0)
            sale.Lines.Add(new IngestLine
            {
                Qty = 1, UnitPricePence = 600, LineGrossPence = 600, VatRateBp = 2000, VatAmountPence = 100,
                DiscountsJson = new LineMeta { ItemIdOne = "5010001" }.ToJson(),
            });
        else
            foreach (var l in lines) sale.Lines.Add(l);

        return sale;
    }

    // ── reading a sale back ──

    /// <summary>⚠ Commit → find → deserialise, byte-for-byte. The payload is what was SENT, so a
    /// reprint shows what the customer was charged rather than a re-derivation of it.</summary>
    [Fact]
    public async Task A_committed_sale_can_be_read_back_in_full()
    {
        var sale = Sale();
        await _store.CommitSaleAsync(sale);

        var found = await _store.FindLocalSaleAsync(sale.SaleId);

        Assert.NotNull(found);
        Assert.Equal(sale.SaleId, found!.SaleId);
        Assert.Equal(600, found.GrossPence);
        Assert.Equal(100, found.VatPence);
        Assert.Equal(sale.BusinessDay, found.BusinessDay);
        Assert.Single(found.Lines);
        Assert.Single(found.Tenders);
    }

    /// <summary>⚠ THE POINT OF THE STEP. A sale stays readable after it has been DELIVERED — the
    /// old behaviour lost it to the till the moment the outbox drained.</summary>
    [Fact]
    public async Task A_sale_stays_readable_after_it_has_been_pushed()
    {
        var sale = Sale();
        var row = await _store.CommitSaleAsync(sale);

        row.Status = (int)OutboxStatus.Pushed;
        row.PushedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        Assert.NotNull(await _store.FindLocalSaleAsync(sale.SaleId));
    }

    [Fact]
    public async Task A_sale_this_till_never_saw_is_null_not_an_error()
    {
        Assert.Null(await _store.FindLocalSaleAsync(Guid.NewGuid()));
        Assert.Null(await _store.FindLocalSaleAsync(Guid.Empty));
    }

    // ── how much has already gone back ──

    /// <summary>
    /// ⚠ THE ONE THAT STOPS A DOUBLE REFUND (binding default 12 — you cannot refund more than was
    /// paid). Two part-refunds of £20 against one £30 sale are each perfectly reasonable alone;
    /// only the running total says the second must be refused.
    /// </summary>
    [Fact]
    public async Task Refunds_against_one_sale_sum_across_every_part_refund()
    {
        var origin = Uuid7.New();

        await _store.CommitSaleAsync(Sale(ReturnLine(origin, 2000)));
        Assert.Equal(2000, await _store.AlreadyRefundedPenceAsync(origin));

        await _store.CommitSaleAsync(Sale(ReturnLine(origin, 500)));
        Assert.Equal(2500, await _store.AlreadyRefundedPenceAsync(origin));
    }

    /// <summary>⚠ A POSITIVE total. Line gross on a return is negative, and a cap compared against
    /// a negative running total lets every refund through.</summary>
    [Fact]
    public async Task The_refunded_total_is_a_positive_magnitude()
    {
        var origin = Uuid7.New();
        await _store.CommitSaleAsync(Sale(ReturnLine(origin, 2000)));

        Assert.True(await _store.AlreadyRefundedPenceAsync(origin) > 0);
    }

    /// <summary>
    /// ⚠ WHY IT IS A TABLE AND NOT A COLUMN. One basket can give back lines from TWO different
    /// original sales. A single origin column would have to pick one and drop the other, and the
    /// dropped one would then look never-refunded — refundable a second time, in full.
    /// </summary>
    [Fact]
    public async Task One_basket_refunding_two_different_sales_records_both()
    {
        var first = Uuid7.New();
        var second = Uuid7.New();

        await _store.CommitSaleAsync(Sale(ReturnLine(first, 1000), ReturnLine(second, 700)));

        Assert.Equal(1000, await _store.AlreadyRefundedPenceAsync(first));
        Assert.Equal(700, await _store.AlreadyRefundedPenceAsync(second));
    }

    /// <summary>Several lines from the same original in one basket add up.</summary>
    [Fact]
    public async Task Several_lines_from_the_same_sale_are_totalled()
    {
        var origin = Uuid7.New();
        await _store.CommitSaleAsync(Sale(ReturnLine(origin, 300), ReturnLine(origin, 450)));

        Assert.Equal(750, await _store.AlreadyRefundedPenceAsync(origin));
    }

    [Fact]
    public async Task A_sale_that_refunds_nothing_records_nothing()
    {
        await _store.CommitSaleAsync(Sale());

        Assert.Equal(0, await _store.AlreadyRefundedPenceAsync(Guid.NewGuid()));
        Assert.Equal(0, await _store.AlreadyRefundedPenceAsync(Guid.Empty));
    }

    /// <summary>
    /// ⚠ A QUEUED REFUND STILL COUNTS. The cash left the drawer when the operator handed it over,
    /// not when the server acknowledged it. Waiting for delivery before counting it would let the
    /// same sale be refunded again while the first refund sits behind a dead network.
    /// </summary>
    [Fact]
    public async Task A_refund_still_queued_in_the_outbox_already_counts()
    {
        var origin = Uuid7.New();
        var refund = Sale(ReturnLine(origin, 1500));
        var row = await _store.CommitSaleAsync(refund);

        Assert.Equal((int)OutboxStatus.Pending, row.Status);
        Assert.Equal(1500, await _store.AlreadyRefundedPenceAsync(origin));
    }

    // ── the schema upgrade ──

    /// <summary>
    /// ⚠ AN EXISTING TILL MUST GAIN THE NEW TABLE. `EnsureCreated` does nothing whatsoever to a
    /// database that already exists, so every table added after a till first ran would simply never
    /// be there — and the symptom is `SQLite Error 1: no such table: LocalRefunds`, at the counter,
    /// on the first return. The version stamp was already being written and nothing read it.
    ///
    /// This simulates a store built before step 15: the table dropped, the stamp left at 2.
    /// </summary>
    [Fact]
    public async Task An_older_store_gains_the_refunds_table_instead_of_failing_at_the_counter()
    {
        await _db.Database.ExecuteSqlRawAsync("DROP TABLE \"LocalRefunds\";");
        await _store.SetMetaAsync(MetaKeys.SchemaVersion, "2");

        await _db.EnsureReadyAsync();

        // It exists AND it works — a table created but not indexed the same way would pass a
        // "does it exist" check and still be the wrong shape.
        var origin = Uuid7.New();
        await _store.CommitSaleAsync(Sale(ReturnLine(origin, 900)));
        Assert.Equal(900, await _store.AlreadyRefundedPenceAsync(origin));

        Assert.Equal(TillDbContext.SchemaVersion.ToString(), await _store.GetMetaAsync(MetaKeys.SchemaVersion));
    }

    /// <summary>
    /// ⚠ An UNSTAMPED store predates the stamp, so it must be treated as OLD, not as current.
    /// Assuming a missing stamp means up-to-date is how an upgrade silently skips.
    ///
    /// ⚠ Uses a FRESH context over the same file, which is how the till really works — one context
    /// per operation. (Reusing this fixture's context hides the bug: `Meta.FindAsync` answers from
    /// the change tracker, so it returns a stamp that raw SQL has already deleted.)
    /// </summary>
    [Fact]
    public async Task An_unstamped_store_is_treated_as_old_and_upgraded()
    {
        await _db.Database.ExecuteSqlRawAsync("DROP TABLE \"LocalRefunds\";");
        await _db.Database.ExecuteSqlRawAsync($"DELETE FROM \"Meta\" WHERE \"Key\" = '{MetaKeys.SchemaVersion}';");

        await using var reopened = new TillDbContext(
            new DbContextOptionsBuilder<TillDbContext>().UseSqlite(_conn).Options);
        await reopened.EnsureReadyAsync();

        var store = new TillStore(reopened);
        var origin = Uuid7.New();
        await store.CommitSaleAsync(Sale(ReturnLine(origin, 100)));
        Assert.Equal(100, await store.AlreadyRefundedPenceAsync(origin));
    }

    /// <summary>⚠ Idempotent. A crash between the DDL and the stamp leaves the step half-done, so
    /// running it again must be harmless rather than "table already exists".</summary>
    [Fact]
    public async Task Running_the_upgrade_twice_is_harmless()
    {
        await _store.SetMetaAsync(MetaKeys.SchemaVersion, "2");
        await _db.EnsureReadyAsync();

        await _store.SetMetaAsync(MetaKeys.SchemaVersion, "2");
        await _db.EnsureReadyAsync();

        var origin = Uuid7.New();
        await _store.CommitSaleAsync(Sale(ReturnLine(origin, 250)));
        Assert.Equal(250, await _store.AlreadyRefundedPenceAsync(origin));
    }

    /// <summary>
    /// ⚠ PRUNING MUST NOT FORGET A REFUND. `PrunePushedAsync` clears delivered sales past the
    /// rolling window; if that took the refund record with it, the running total would drop back to
    /// zero and the original would be refundable all over again. There is deliberately no cascade.
    /// </summary>
    [Fact]
    public async Task Pruning_a_delivered_refund_does_not_forget_that_it_happened()
    {
        var origin = Uuid7.New();
        var row = await _store.CommitSaleAsync(Sale(ReturnLine(origin, 1200)));

        row.Status = (int)OutboxStatus.Pushed;
        row.PushedAtUtc = DateTime.UtcNow.AddDays(-90);
        await _db.SaveChangesAsync();

        Assert.Equal(1, await _store.PrunePushedAsync(TimeSpan.FromDays(30)));
        Assert.Null(await _store.FindLocalSaleAsync(row.SaleId));      // the sale is gone
        Assert.Equal(1200, await _store.AlreadyRefundedPenceAsync(origin));  // the money is not
    }

    // ── finding a sale AT ALL (2026-08-10) ──
    //
    // ⚠ The refund rule has been complete since steps 15–17. What was missing was the door: the
    // only way to name a sale was `FindLocalSaleAsync(Guid)`, and nothing in the app could produce
    // a Guid — no list, no search. The id's only source was the barcode on a printed receipt, so a
    // till with no printer could not refund anything. Reported as "In MAUI I cannot do a refund?".

    [Fact]
    public async Task Recent_sales_come_back_NEWEST_first()
    {
        var oldest = Sale(); oldest.OccurredAtUtc = DateTime.UtcNow.AddMinutes(-30);
        var middle = Sale(); middle.OccurredAtUtc = DateTime.UtcNow.AddMinutes(-20);
        var newest = Sale(); newest.OccurredAtUtc = DateTime.UtcNow.AddMinutes(-10);

        await _store.CommitSaleAsync(oldest);
        await _store.CommitSaleAsync(newest);
        await _store.CommitSaleAsync(middle);

        var recent = await _store.ListRecentSalesAsync();

        Assert.Equal(
            new[] { newest.SaleId, middle.SaleId, oldest.SaleId },
            recent.Select(s => s.SaleId).ToArray());
    }

    /// <summary>The summary must carry enough to RECOGNISE a sale — the money, and what was on it.</summary>
    [Fact]
    public async Task A_recent_sale_carries_what_an_operator_recognises_it_by()
    {
        var sale = Sale();
        await _store.CommitSaleAsync(sale);

        var found = Assert.Single(await _store.ListRecentSalesAsync());

        Assert.Equal(sale.SaleId, found.SaleId);
        Assert.Equal(600, found.GrossPence);
        Assert.Equal(1, found.LineCount);
        Assert.Equal("5010001", found.FirstItemIdOne);
    }

    /// <summary>
    /// ⚠ A QUEUED SALE IS REFUNDABLE. The money left the drawer when the goods were handed over,
    /// whatever the outbox has managed to deliver — so this list must not filter on status. Hiding
    /// unsent sales would make a shop unable to refund anything it sold while the line was down,
    /// which is exactly when it will need to.
    /// </summary>
    [Fact]
    public async Task A_sale_still_QUEUED_is_offered_for_refund()
    {
        var sale = Sale();
        var row = await _store.CommitSaleAsync(sale);
        Assert.Equal((int)OutboxStatus.Pending, row.Status);

        var found = Assert.Single(await _store.ListRecentSalesAsync());
        Assert.Equal(sale.SaleId, found.SaleId);
    }

    [Fact]
    public async Task The_list_is_capped_at_what_was_asked_for()
    {
        for (var i = 0; i < 8; i++)
        {
            var s = Sale();
            s.OccurredAtUtc = DateTime.UtcNow.AddMinutes(-i);
            await _store.CommitSaleAsync(s);
        }

        Assert.Equal(3, (await _store.ListRecentSalesAsync(3)).Count);
    }

    // ── the double refund, 2026-08-10 ──
    //
    // ⚠ £13.99 LEFT THE DRAWER TWICE ON A £13.99 SALE. A refund is stored as its OWN sale with a
    // negative gross, so it appeared in the recent-sales picker — newest first, right where the
    // operator taps — and the second refund was taken AGAINST THE FIRST REFUND. The server recorded
    // the adjustment against the refund's own id, so the per-sale cap had nothing to compare with.

    /// <summary>⚠ A refund must never be offered as something to refund against.</summary>
    [Fact]
    public async Task Refunds_are_NOT_offered_as_sales_to_refund_against()
    {
        var origin = Uuid7.New();
        await _store.CommitSaleAsync(Sale());   // a purchase

        // ⚠ Built with a NEGATIVE header, which is what `SaleAssembler` really produces for a
        // return and what the live data showed (-1399). The shared `Sale()` helper hardcodes a
        // positive gross, so using it here would have tested nothing.
        var refund = Sale(ReturnLine(origin, 600));
        refund.GrossPence = -600;
        refund.VatPence = -100;
        await _store.CommitSaleAsync(refund);

        Assert.Equal(2, (await _store.ListRecentSalesAsync()).Count);

        var refundable = await _store.ListRecentSalesAsync(purchasesOnly: true);
        var only = Assert.Single(refundable);
        Assert.True(only.GrossPence > 0, "a negative-gross sale is a refund and cannot be refunded");
    }

    /// <summary>
    /// ⚠ THE DRAIN WINDOW. `ReturnLookup` prefers the SERVER's figure, but the platform only counts
    /// refunds it has RECEIVED — and a refund sits in the outbox for up to a minute. The two real
    /// refunds on 2026-08-10 were 97 milliseconds apart and drained together, so the server answered
    /// "nothing refunded" both times, entirely correctly, while this till knew all along.
    /// </summary>
    [Fact]
    public async Task A_refund_still_QUEUED_counts_against_the_next_one()
    {
        var origin = Uuid7.New();
        var refund = await _store.CommitSaleAsync(Sale(ReturnLine(origin, 900)));

        Assert.Equal((int)OutboxStatus.Pending, refund.Status);
        Assert.Equal(900, await _store.UndeliveredRefundedPenceAsync(origin));
    }

    /// <summary>⚠ Once DELIVERED it is the server's to count, and counting it here too would double
    /// it — refusing a legitimate refund of something else on the same sale.</summary>
    [Fact]
    public async Task A_refund_the_platform_has_TAKEN_is_no_longer_counted_locally()
    {
        var origin = Uuid7.New();
        var refund = await _store.CommitSaleAsync(Sale(ReturnLine(origin, 900)));

        refund.Status = (int)OutboxStatus.Pushed;
        await _db.SaveChangesAsync();

        Assert.Equal(0, await _store.UndeliveredRefundedPenceAsync(origin));
        Assert.Equal(900, await _store.AlreadyRefundedPenceAsync(origin));   // the full record stands
    }

    /// <summary>
    /// ⚠ A REJECTED refund still counts. The money left the drawer when the goods were handed over,
    /// whatever the platform decided afterwards — treating a 400 as "not refunded" would hand it
    /// over a second time while somebody is investigating the first.
    /// </summary>
    [Fact]
    public async Task A_refund_the_platform_REFUSED_still_counts_against_the_next_one()
    {
        var origin = Uuid7.New();
        var refund = await _store.CommitSaleAsync(Sale(ReturnLine(origin, 900)));

        refund.Status = (int)OutboxStatus.Failed;
        await _db.SaveChangesAsync();

        Assert.Equal(900, await _store.UndeliveredRefundedPenceAsync(origin));
    }

    /// <summary>
    /// ⚠ A sale whose payload will not parse is SKIPPED, not shown as a blank row. Offering a sale
    /// that cannot then be read back is offering a refund that will fail at the counter.
    /// </summary>
    [Fact]
    public async Task A_sale_whose_payload_is_corrupt_is_left_OUT_rather_than_shown_blank()
    {
        var good = Sale();
        await _store.CommitSaleAsync(good);

        var bad = await _store.CommitSaleAsync(Sale());
        bad.PayloadJson = "{ this is not json";
        await _db.SaveChangesAsync();

        var recent = await _store.ListRecentSalesAsync();

        Assert.Equal(good.SaleId, Assert.Single(recent).SaleId);
    }
}
