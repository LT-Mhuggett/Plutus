using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Client.Core;
using Plutus.Client.Storage;
using Plutus.Contracts.Client;
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
}
