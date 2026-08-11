using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Client.Storage;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// A Z-closed day takes no more money.
///
/// ⚠⚠ Matt, 2026-08-11: *"I was able to make a sale with the till closed, but it doesn't look like
/// the sale was captured. I was also able to refund it."*
///
/// He was right, and the truth was worse than his reading of it: **the sale WAS captured.** The
/// day-closed gate existed only on the CASH-EVENT path — on the till and on the server — and nothing
/// on the sales path asked at all. So a sale rung after a Z-read committed locally, drained
/// normally, and the server accepted it as 201 Recorded against a day whose takings had already
/// been counted and banked.
///
/// ⚠ THAT is the damage. A lost sale is one problem. A sale added to a day that has already been
/// reconciled means the Z-read, the banking and the platform's figures disagree for ever, with
/// nothing anywhere flagging it — the variance turns up weeks later as an unexplained discrepancy.
///
/// These tests pin the STORE-level fact the gate reads. The gate itself lives in
/// `CheckoutCommit.CommitAsync`, which needs a device to exercise.
/// </summary>
public class DayClosedGateTests
{
    private static async Task<(TillStore Store, SqliteConnection Conn)> NewStoreAsync()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<TillDbContext>().UseSqlite(conn).Options;
        var db = new TillDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return (new TillStore(db), conn);
    }

    private static readonly DateOnly Day = new(2026, 8, 11);

    [Fact]
    public async Task An_open_day_is_not_closed()
    {
        var (store, conn) = await NewStoreAsync();
        using var _ = conn;

        Assert.False(await store.IsDayClosedAsync(BusinessDay.Wire(Day)));
    }

    [Fact]
    public async Task A_Z_close_closes_the_day()
    {
        var (store, conn) = await NewStoreAsync();
        using var _ = conn;

        await store.RecordCashEventAsync(
            CashEventTypes.ZClose, BusinessDay.Wire(Day), amountPence: 0, countedPence: 11000, reason: "close");

        Assert.True(await store.IsDayClosedAsync(BusinessDay.Wire(Day)));
    }

    [Fact]
    public async Task An_X_snapshot_does_NOT_close_the_day()
    {
        // ⚠ An X is a mid-shift count. If it closed the day, a manager checking the drawer at
        // lunchtime would stop the shop trading — and the till would say the day was over.
        var (store, conn) = await NewStoreAsync();
        using var _ = conn;

        await store.RecordCashEventAsync(
            CashEventTypes.XSnapshot, BusinessDay.Wire(Day), amountPence: 0, countedPence: 13000, reason: "mid-shift");

        Assert.False(await store.IsDayClosedAsync(BusinessDay.Wire(Day)));
    }

    [Fact]
    public async Task Closing_YESTERDAY_does_not_close_TODAY()
    {
        // ⚠ The gate is per business day, so a till that was closed last night must open normally.
        // Getting this wrong would brick every till at the start of every day.
        var (store, conn) = await NewStoreAsync();
        using var _ = conn;

        await store.RecordCashEventAsync(
            CashEventTypes.ZClose, BusinessDay.Wire(Day.AddDays(-1)), amountPence: 0, countedPence: 11000, reason: "close");

        Assert.False(await store.IsDayClosedAsync(BusinessDay.Wire(Day)));
        Assert.True(await store.IsDayClosedAsync(BusinessDay.Wire(Day.AddDays(-1))));
    }

    [Fact]
    public void The_gate_and_the_cash_events_must_agree_on_the_DAY_STRING()
    {
        // ⚠⚠ THE QUIETEST WAY FOR THIS GUARD TO DO NOTHING. `IsDayClosedAsync` takes a STRING and
        // the cash events are stored under `BusinessDay.Wire`. A caller formatting the date any
        // other way finds no Z close, concludes the day is open, and lets the sale through — with no
        // error anywhere. So the wire format is pinned.
        Assert.Equal("2026-08-11", BusinessDay.Wire(Day));
    }
}
