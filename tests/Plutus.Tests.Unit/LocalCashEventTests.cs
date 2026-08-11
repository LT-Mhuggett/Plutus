using System;
using System.Linq;
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
/// WP9 / cutover step 23 — the till's own record of its drawer.
///
/// ⚠ WHY ANY OF THIS IS LOCAL. A shop opens before its broadband does. Declaring the opening float,
/// taking a paid-out for a supplier and closing the day are things a till must be able to DO with
/// the line down, because the money moves whether or not the platform hears about it. A float that
/// failed to post is a day whose banking cannot be reconciled at all.
/// </summary>
public class LocalCashEventTests : IAsyncLifetime
{
    private SqliteConnection _conn = null!;
    private TillDbContext _db = null!;
    private TillStore _store = null!;

    private const string Today = "2026-08-10";
    private const string Tomorrow = "2026-08-11";

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

    [Fact]
    public async Task An_opening_float_is_recorded_and_queued()
    {
        var row = await _store.RecordCashEventAsync(CashEventTypes.OpenFloat, Today, 15000);

        Assert.NotNull(row);
        Assert.NotEqual(Guid.Empty, row!.EventId);
        Assert.Equal((int)OutboxStatus.Pending, row.Status);
        Assert.Single(await _store.PendingCashEventsAsync());
    }

    /// <summary>
    /// ⚠ ONE Z PER BUSINESS DAY, enforced HERE as well as on the server. A Z-close is the statement
    /// "this is what the drawer held when we finished"; anything recorded against that day
    /// afterwards makes the statement false. The server 409s it anyway — refusing locally means the
    /// operator is told at the counter instead of discovering it in a report tomorrow, and it works
    /// with the line down, which is exactly when the server's guard cannot be consulted.
    /// </summary>
    [Fact]
    public async Task Nothing_can_be_recorded_against_a_day_that_has_been_Z_closed()
    {
        await _store.RecordCashEventAsync(CashEventTypes.OpenFloat, Today, 15000);
        Assert.NotNull(await _store.RecordCashEventAsync(CashEventTypes.ZClose, Today, 0, countedPence: 41250));

        Assert.True(await _store.IsDayClosedAsync(Today));

        // every type, not merely a second Z
        Assert.Null(await _store.RecordCashEventAsync(CashEventTypes.ZClose, Today, 0, countedPence: 1));
        Assert.Null(await _store.RecordCashEventAsync(CashEventTypes.PaidOut, Today, 500, reason: "late supplier"));
        Assert.Null(await _store.RecordCashEventAsync(CashEventTypes.XSnapshot, Today, 0, countedPence: 100));
        Assert.Null(await _store.RecordCashEventAsync(CashEventTypes.OpenFloat, Today, 10000));
    }

    /// <summary>⚠ Closing TODAY must not close TOMORROW. The guard is per business day, and a till
    /// that refused every subsequent day would need reinstalling each morning.</summary>
    [Fact]
    public async Task Closing_one_day_leaves_the_next_day_open()
    {
        await _store.RecordCashEventAsync(CashEventTypes.ZClose, Today, 0, countedPence: 41250);

        Assert.True(await _store.IsDayClosedAsync(Today));
        Assert.False(await _store.IsDayClosedAsync(Tomorrow));
        Assert.NotNull(await _store.RecordCashEventAsync(CashEventTypes.OpenFloat, Tomorrow, 15000));
    }

    /// <summary>⚠ An X-read is a COUNT, not a close. Taking one must leave the day tradeable —
    /// otherwise a mid-shift check would end the shift.</summary>
    [Fact]
    public async Task An_X_read_does_not_close_the_day()
    {
        await _store.RecordCashEventAsync(CashEventTypes.XSnapshot, Today, 0, countedPence: 22000);

        Assert.False(await _store.IsDayClosedAsync(Today));
        Assert.NotNull(await _store.RecordCashEventAsync(CashEventTypes.PaidIn, Today, 500, reason: "change from safe"));
    }

    [Fact]
    public async Task The_days_history_comes_back_oldest_first()
    {
        var t0 = new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc);
        await _store.RecordCashEventAsync(CashEventTypes.OpenFloat, Today, 15000, occurredAtUtc: t0);
        await _store.RecordCashEventAsync(CashEventTypes.PaidOut, Today, 2000, reason: "window cleaner", occurredAtUtc: t0.AddHours(3));
        await _store.RecordCashEventAsync(CashEventTypes.XSnapshot, Today, 0, countedPence: 30000, occurredAtUtc: t0.AddHours(5));

        var history = await _store.CashEventsForDayAsync(Today);

        Assert.Equal(
            new[] { CashEventTypes.OpenFloat, CashEventTypes.PaidOut, CashEventTypes.XSnapshot },
            history.Select(h => h.Type).ToArray());
    }

    /// <summary>
    /// ⚠ A REFUSAL IS TERMINAL AND KEEPS THE SERVER'S WORDS. A 409 "already Z-closed" cannot be
    /// fixed by asking again; a till that retried it for ever would look healthy while quietly never
    /// banking. Somebody has to explain it tomorrow, so the reason is stored.
    /// </summary>
    [Fact]
    public async Task A_refused_event_leaves_the_queue_and_keeps_why()
    {
        var row = await _store.RecordCashEventAsync(CashEventTypes.PaidOut, Today, 2000, reason: "supplier");

        await _store.SettleCashEventAsync(row!.EventId, OutboxStatus.Failed, "{\"status\":409}");

        Assert.Empty(await _store.PendingCashEventsAsync());
        var stored = (await _store.CashEventsForDayAsync(Today)).Single();
        Assert.Equal((int)OutboxStatus.Failed, stored.Status);
        Assert.Contains("409", stored.ServerResponseJson);
        Assert.Equal(1, stored.Attempts);
    }

    [Fact]
    public async Task A_sent_event_leaves_the_queue_and_is_stamped()
    {
        var row = await _store.RecordCashEventAsync(CashEventTypes.OpenFloat, Today, 15000);

        await _store.SettleCashEventAsync(row!.EventId, OutboxStatus.Pushed);

        Assert.Empty(await _store.PendingCashEventsAsync());
        var stored = (await _store.CashEventsForDayAsync(Today)).Single();
        Assert.Equal((int)OutboxStatus.Pushed, stored.Status);
        Assert.NotNull(stored.PushedAtUtc);
    }

    /// <summary>
    /// ⚠ THE EVENT ID IS THE TILL'S, and it is what makes the drain safe to retry: the server
    /// replays a known id back as 200 with the stored outcome, so an event posted twice because the
    /// line dropped mid-request is recorded once. Two events must never share one.
    /// </summary>
    [Fact]
    public async Task Every_event_gets_its_own_id()
    {
        var a = await _store.RecordCashEventAsync(CashEventTypes.PaidIn, Today, 100, reason: "a");
        var b = await _store.RecordCashEventAsync(CashEventTypes.PaidIn, Today, 100, reason: "b");

        Assert.NotEqual(a!.EventId, b!.EventId);
    }

    /// <summary>
    /// ⚠ AN EXISTING TILL MUST GAIN THE TABLE, not fail at the counter on the morning it upgrades.
    ///
    /// Every till in the field is on schema 3. The first thing the operator does after installing
    /// this build is declare the opening float — so if the v4 step does not run, the very first
    /// cash action of the day throws "no such table: LocalCashEvents", with a queue of customers.
    ///
    /// Simulates a pre-WP9 store: drop the table, wind the stamp back to 3.
    /// </summary>
    [Fact]
    public async Task An_older_store_gains_the_cash_table_instead_of_failing_at_the_counter()
    {
        await _db.Database.ExecuteSqlRawAsync("DROP TABLE \"LocalCashEvents\";");
        await _store.SetMetaAsync(MetaKeys.SchemaVersion, "3");

        await _db.EnsureReadyAsync();

        // Exists AND works — a table created without its indexes passes a "does it exist" check and
        // is still the wrong shape.
        var row = await _store.RecordCashEventAsync(CashEventTypes.OpenFloat, Today, 15000);
        Assert.NotNull(row);
        Assert.Single(await _store.CashEventsForDayAsync(Today));
        Assert.False(await _store.IsDayClosedAsync(Today));

        Assert.Equal(TillDbContext.SchemaVersion.ToString(), await _store.GetMetaAsync(MetaKeys.SchemaVersion));
    }

    /// <summary>⚠ The five type names travel to the server as STRINGS and are parsed there with
    /// `Enum.TryParse&lt;CashEventType&gt;`. A rename on either side is a 400 — a till that cannot
    /// bank — so the vocabulary is pinned.</summary>
    [Fact]
    public void The_wire_vocabulary_is_exactly_the_five_the_server_parses()
    {
        Assert.Equal(
            new[] { "OpenFloat", "PaidIn", "PaidOut", "XSnapshot", "ZClose" },
            CashEventTypes.All);

        Assert.True(CashEventTypes.NeedsCount(CashEventTypes.XSnapshot));
        Assert.True(CashEventTypes.NeedsCount(CashEventTypes.ZClose));
        Assert.False(CashEventTypes.NeedsCount(CashEventTypes.OpenFloat));

        Assert.True(CashEventTypes.NeedsReason(CashEventTypes.PaidIn));
        Assert.True(CashEventTypes.NeedsReason(CashEventTypes.PaidOut));
        Assert.False(CashEventTypes.NeedsReason(CashEventTypes.ZClose));
    }

    // ── the platform's verdict on a counted drawer (finding I, 2026-08-11) ──

    /// <summary>
    /// The platform's expected/variance figures are RECORDED, and they survive.
    ///
    /// ⚠ Matt: *"If the Zclose is a different number than expected e.g. opened with £150, spent £20
    /// and close with £110. This should be flagged."* The server had always answered the POST with
    /// both figures; `CashPushService` kept the status code and threw the body away, so a drawer
    /// £20 short was accepted with a 201 and the operator was told nothing.
    ///
    /// ⚠ STORED, not fetched on demand — it has to be readable with the line down, which is exactly
    /// the shift where somebody will have to explain the difference in the morning.
    /// </summary>
    [Fact]
    public async Task The_platforms_verdict_on_a_counted_drawer_is_kept()
    {
        var z = await _store.RecordCashEventAsync(CashEventTypes.ZClose, Today, 0, countedPence: 11000);
        Assert.NotNull(z);

        // Opened with £150, £20 of paid-outs, so Plutus expected £130 and counted £110 is £20 short.
        await _store.SettleCashEventAsync(
            z!.EventId, OutboxStatus.Pushed, expectedPence: 13000, variancePence: -2000);

        var stored = (await _store.CashEventsForDayAsync(Today)).Single();
        Assert.Equal(13000, stored.ExpectedPence);
        Assert.Equal(-2000, stored.VariancePence);

        // ⚠ NEGATIVE IS SHORT. The sign carries the direction and inverting it would tell an
        // operator their till is over when the money is missing.
        Assert.True(stored.VariancePence < 0);
    }

    /// <summary>
    /// ⚠ A LATER SETTLE WITHOUT FIGURES MUST NOT ERASE THE ONES WE HAVE. `SettleCashEventAsync` is
    /// called on every attempt, and most callers pass nothing — a plain `=` would blank the
    /// platform's verdict the next time anything touched the row, leaving a drawer that was £20
    /// short looking as though it had never been judged at all.
    /// </summary>
    [Fact]
    public async Task A_later_settle_without_figures_does_not_erase_the_verdict()
    {
        var z = await _store.RecordCashEventAsync(CashEventTypes.ZClose, Today, 0, countedPence: 11000);

        await _store.SettleCashEventAsync(
            z!.EventId, OutboxStatus.Pushed, expectedPence: 13000, variancePence: -2000);
        await _store.SettleCashEventAsync(z.EventId, OutboxStatus.Pushed);

        var stored = (await _store.CashEventsForDayAsync(Today)).Single();
        Assert.Equal(13000, stored.ExpectedPence);
        Assert.Equal(-2000, stored.VariancePence);
    }

    /// <summary>
    /// ⚠ A BALANCING DRAWER RECORDS A ZERO, AND ZERO IS NOT "UNKNOWN". The screen shows "✅ balances"
    /// for 0 and nothing at all for null, so collapsing the two would either claim every unsent Z
    /// balanced or refuse to confirm the ones that did.
    /// </summary>
    [Fact]
    public async Task A_drawer_that_balances_records_zero_not_null()
    {
        var z = await _store.RecordCashEventAsync(CashEventTypes.ZClose, Today, 0, countedPence: 13000);

        await _store.SettleCashEventAsync(
            z!.EventId, OutboxStatus.Pushed, expectedPence: 13000, variancePence: 0);

        var stored = (await _store.CashEventsForDayAsync(Today)).Single();
        Assert.Equal(0, stored.VariancePence);
        Assert.NotNull(stored.VariancePence);
    }

    /// <summary>
    /// ⚠⚠ THE Z MUST NOT OVERTAKE ITS OWN DAY'S SALES, and this is the rule that makes the variance
    /// mean anything at all.
    ///
    /// The platform computes the expected drawer as float + **cash takings** + ins − outs, where the
    /// takings half is the sales it has actually RECEIVED. Push the Z while this till still has
    /// sales queued and the answer is a shortage equal to every penny not yet sent — a till that
    /// traded £400 through an outage would report itself £400 down to the person who counted it
    /// correctly, in red.
    ///
    /// ⚠ And the Z is TERMINAL server-side: everything against the day is refused afterwards, so a Z
    /// that overtakes its sales also lands them in quarantine behind their own close.
    /// </summary>
    [Fact]
    public async Task A_days_unsent_sales_are_visible_to_the_drain_that_holds_its_Z()
    {
        var sale = new IngestSaleRequest
        {
            SaleId = Uuid7.New(),
            BusinessDay = DateOnly.Parse(Today),
            OccurredAtUtc = DateTime.UtcNow,
            GrossPence = 600,
            VatPence = 100,
            Tenders = { new IngestTender { TenderType = Tenders.Cash, AmountPence = 600 } },
        };
        sale.Lines.Add(new IngestLine
        {
            Qty = 1, UnitPricePence = 600, LineGrossPence = 600, VatRateBp = 2000, VatAmountPence = 100,
        });

        await _store.CommitSaleAsync(sale);

        Assert.Equal(1, await _store.PendingSalesForDayAsync(Today));

        // ⚠ SCOPED TO THE DAY. A sale queued for today must not hold up tomorrow's close, and — the
        // case that matters more — one stuck sale from last week must never block every Z from now
        // on. It cannot affect today's expected drawer, so it gets no vote on today's close.
        Assert.Equal(0, await _store.PendingSalesForDayAsync(Tomorrow));

        // Once it has gone, nothing holds the Z back — otherwise the guard would be a deadlock
        // rather than a delay, and a till that can never close its day is worse than a variance.
        var queued = (await _store.GetPendingAsync(10)).Single();
        queued.Status = OutboxStatus.Pushed;
        await _store.UpdateAsync(queued);

        Assert.Equal(0, await _store.PendingSalesForDayAsync(Today));
    }

    /// <summary>
    /// ⚠ A SALE THE PLATFORM HAS REFUSED DOES NOT HOLD THE Z BACK — a trade, and it is stated
    /// rather than left to be discovered.
    ///
    /// A Failed sale is terminal: it will never send. Counting it here would block this till's Z
    /// close **for ever**, and a till that cannot close its day is a worse outcome than one that
    /// closes with a variance somebody can explain in the morning.
    ///
    /// ⚠ The cost is real and worth naming: that sale's cash then shows as a shortage. That is the
    /// correct SHAPE of the problem — the money genuinely is unaccounted for at the platform — and
    /// the refusal itself is already reported on the Plutus tab.
    ///
    /// ⚠ This test exists because the mutation survived. Changing `== Pending` to `!= Pushed` broke
    /// nothing, which meant the one rule standing between a refused sale and a till that can never
    /// close a day was undefended.
    /// </summary>
    [Fact]
    public async Task A_sale_the_platform_REFUSED_does_not_block_the_Z_for_ever()
    {
        var sale = new IngestSaleRequest
        {
            SaleId = Uuid7.New(),
            BusinessDay = DateOnly.Parse(Today),
            OccurredAtUtc = DateTime.UtcNow,
            GrossPence = 600,
            VatPence = 100,
            Tenders = { new IngestTender { TenderType = Tenders.Cash, AmountPence = 600 } },
        };
        sale.Lines.Add(new IngestLine
        {
            Qty = 1, UnitPricePence = 600, LineGrossPence = 600, VatRateBp = 2000, VatAmountPence = 100,
        });

        await _store.CommitSaleAsync(sale);

        var queued = (await _store.GetPendingAsync(10)).Single();
        queued.Status = OutboxStatus.Failed;
        await _store.UpdateAsync(queued);

        // Failed, not Pending — so the Z goes, and the shortage is the honest consequence.
        Assert.Equal(0, await _store.PendingSalesForDayAsync(Today));
    }

    /// <summary>
    /// ⚠⚠ THE REAL `ALTER TABLE ADD COLUMN` PATH — the one every existing till takes on the way to
    /// v6, and the one no other test reaches.
    ///
    /// `Running_the_upgrade_twice_is_harmless` drops the table and lets `CREATE TABLE IF NOT EXISTS`
    /// rebuild it from the CURRENT DDL, which already has the new columns — so the guard finds them
    /// present and returns, and the ADD COLUMN statements are never executed at all. A till in a
    /// shop has a v5 table with the old shape and genuinely runs them.
    ///
    /// ⚠ THIS FAILS AT START-UP, which is a till that will not open on a Monday morning. SQLite has
    /// no `ADD COLUMN IF NOT EXISTS`, so a wrong column definition or a missing guard is not a bad
    /// figure on a screen — it is a shop that cannot sell.
    ///
    /// ⚠ AND THE OLD ROWS SURVIVE WITH A NULL VERDICT, which is the truthful record: for a Z closed
    /// before v6 the platform's answer was never kept, and inventing one now would put a figure
    /// against a past close that nobody actually saw on the night.
    /// </summary>
    [Fact]
    public async Task An_old_store_gains_the_verdict_columns_without_losing_its_history()
    {
        // Rebuild LocalCashEvents in its v5 shape — no ExpectedPence, no VariancePence.
        await _db.Database.ExecuteSqlRawAsync("DROP TABLE \"LocalCashEvents\";");
        await _db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE "LocalCashEvents" (
                "EventId" TEXT NOT NULL CONSTRAINT "PK_LocalCashEvents" PRIMARY KEY,
                "Type" TEXT NOT NULL,
                "BusinessDay" TEXT NOT NULL,
                "OccurredAtUtc" TEXT NOT NULL,
                "AmountPence" INTEGER NOT NULL,
                "CountedPence" INTEGER NULL,
                "Reason" TEXT NULL,
                "OperatorUserId" TEXT NULL,
                "Status" INTEGER NOT NULL,
                "PushedAtUtc" TEXT NULL,
                "Attempts" INTEGER NOT NULL DEFAULT 0,
                "ServerResponseJson" TEXT NULL
            );
            """);

        // A float this till recorded before the upgrade.
        await _db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO "LocalCashEvents"
                ("EventId","Type","BusinessDay","OccurredAtUtc","AmountPence","Status","Attempts")
            VALUES ('{Guid.NewGuid()}','{CashEventTypes.OpenFloat}','{Today}','2026-08-10T08:00:00',15000,1,1);
            """);

        await _store.SetMetaAsync(MetaKeys.SchemaVersion, "5");
        await _db.EnsureReadyAsync();

        // The history is still there, and the columns now exist and read null.
        var old = (await _store.CashEventsForDayAsync(Today)).Single();
        Assert.Equal(15000, old.AmountPence);
        Assert.Null(old.ExpectedPence);
        Assert.Null(old.VariancePence);

        // ⚠ And replaying the upgrade is harmless — a crash between the DDL and the stamp leaves it
        // half done, and SQLite would otherwise throw "duplicate column name" on the retry.
        await _store.SetMetaAsync(MetaKeys.SchemaVersion, "5");
        await _db.EnsureReadyAsync();

        var z = await _store.RecordCashEventAsync(CashEventTypes.ZClose, Today, 0, countedPence: 11000);
        await _store.SettleCashEventAsync(
            z!.EventId, OutboxStatus.Pushed, expectedPence: 13000, variancePence: -2000);

        Assert.Equal(-2000, (await _store.CashEventsForDayAsync(Today)).Last().VariancePence);
        Assert.Equal(TillDbContext.SchemaVersion.ToString(), await _store.GetMetaAsync(MetaKeys.SchemaVersion));
    }
}
