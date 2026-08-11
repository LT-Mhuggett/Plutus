using System;
using System.Linq;
using Plutus.Tenancy;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP5 — how present a till is, and the cursor the changes feed pages by.
///
/// Both are small and both are the kind of small that goes wrong quietly: an off-by-one on a
/// presence boundary makes the fleet list flap, and an off-by-one in the cursor loses an item's new
/// price on every till with no error anywhere.
/// </summary>
public class TillPresenceTests
{
    private static readonly DateTime T0 = new(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);

    private static (TillPresence Presence, Func<DateTime> Clock) At(Func<DateTime> clock) =>
        (new TillPresence(clock), clock);

    [Fact]
    public void A_till_that_has_never_beaten_is_Offline_not_unknown()
    {
        // "Unknown" would be a third state every caller has to handle, and the honest answer is
        // simpler: a till nobody has heard from is not online.
        var presence = new TillPresence(() => T0);
        Assert.Equal(PresenceState.Offline, presence.StateOf(Guid.NewGuid()));
        Assert.Null(presence.Get(Guid.NewGuid()));
    }

    [Theory]
    [InlineData(0, PresenceState.Online)]
    [InlineData(119, PresenceState.Online)]
    [InlineData(121, PresenceState.Stale)]
    [InlineData(299, PresenceState.Stale)]
    [InlineData(301, PresenceState.Offline)]
    public void The_boundaries_sit_at_two_and_five_minutes(int secondsAgo, PresenceState expected)
    {
        var now = T0;
        var presence = new TillPresence(() => now);
        var deviceId = Guid.NewGuid();

        presence.Record(deviceId, Guid.NewGuid(), "1.0.0", 0, null, now);
        now = T0.AddSeconds(secondsAgo);

        Assert.Equal(expected, presence.StateOf(deviceId));
    }

    [Fact]
    public void A_beat_carries_the_numbers_a_manager_actually_wants()
    {
        // Outbox depth alone hides the difference between 20 sales from this hour and 3 stuck since
        // Tuesday — which is the difference between "busy" and "go and look at that till".
        var presence = new TillPresence(() => T0);
        var deviceId = Guid.NewGuid();
        var tillId = Guid.NewGuid();

        presence.Record(deviceId, tillId, "1.2.3", outboxDepth: 20, oldestUnsyncedAgeSeconds: 86_400, deviceClockUtc: T0);

        var entry = presence.Get(deviceId)!;
        Assert.Equal(tillId, entry.TillId);
        Assert.Equal("1.2.3", entry.AppVersion);
        Assert.Equal(20, entry.OutboxDepth);
        Assert.Equal(86_400, entry.OldestUnsyncedAgeSeconds);
    }

    [Fact]
    public void Clock_drift_is_recorded_so_it_can_be_seen_centrally()
    {
        // A till an hour out has its sales judged against a different instant than it thinks —
        // VAT bands are effective-dated and tokens expire. Nothing else would ever surface it.
        var presence = new TillPresence(() => T0);
        var deviceId = Guid.NewGuid();

        presence.Record(deviceId, Guid.NewGuid(), null, 0, null, T0.AddHours(1));

        Assert.Equal(TimeSpan.FromHours(1), presence.Get(deviceId)!.ClockSkew);
    }

    [Fact]
    public void A_later_beat_replaces_the_earlier_one()
    {
        var now = T0;
        var presence = new TillPresence(() => now);
        var deviceId = Guid.NewGuid();

        presence.Record(deviceId, Guid.NewGuid(), "1.0.0", 5, null, now);
        now = T0.AddMinutes(10);
        Assert.Equal(PresenceState.Offline, presence.StateOf(deviceId));

        presence.Record(deviceId, Guid.NewGuid(), "1.0.1", 0, null, now);
        Assert.Equal(PresenceState.Online, presence.StateOf(deviceId));
        Assert.Equal(0, presence.Get(deviceId)!.OutboxDepth);
    }

    [Fact]
    public void All_lists_every_known_till_newest_first()
    {
        var now = T0;
        var presence = new TillPresence(() => now);
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();

        presence.Record(older, Guid.NewGuid(), null, 0, null, now);
        now = T0.AddSeconds(30);
        presence.Record(newer, Guid.NewGuid(), null, 0, null, now);

        var all = presence.All();
        Assert.Equal(2, all.Count);
        Assert.Equal(newer, all[0].DeviceId);
    }
}

/// <summary>The catalogue feed's keyset cursor.</summary>
public class CatalogueCursorTests
{
    [Fact]
    public void A_cursor_round_trips()
    {
        var at = new DateTime(2026, 8, 8, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234);
        var encoded = CatalogueCursor.Encode(at, "5012345678900");

        Assert.True(CatalogueCursor.TryDecode(encoded, out var back, out var idOne));
        Assert.Equal(at, back);
        Assert.Equal("5012345678900", idOne);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-cursor")]
    [InlineData(":5012345678900")]
    [InlineData("99999999999999999999:x")]
    public void A_broken_cursor_means_start_from_the_beginning(string? cursor)
    {
        // ⚠ Fails OPEN, deliberately. A full resync is always correct and merely expensive; refusing
        // to sync would leave a till with a corrupted cursor permanently stale, and silently.
        Assert.False(CatalogueCursor.TryDecode(cursor, out _, out _));
    }

    [Fact]
    public void A_barcode_containing_the_separator_still_round_trips()
    {
        // The tick count cannot contain ':', so splitting on the FIRST one is safe however odd the
        // barcode is. Worth pinning: a barcode is legacy free text, not a validated format.
        var at = new DateTime(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(CatalogueCursor.TryDecode(CatalogueCursor.Encode(at, "AB:CD:EF"), out var back, out var idOne));
        Assert.Equal(at, back);
        Assert.Equal("AB:CD:EF", idOne);
    }
}

/// <summary>
/// The DURABLE "last online" — `Device.LastSeenUtc`, written by the heartbeat on a throttle.
///
/// ⚠ Matt, 2026-08-11: *"I expect the 'Last online' to be updated via the heartbeat? It's showing
/// tills last online days ago?"* It was showing `Till.LastOnline`, written twice in the whole
/// codebase — both at ENROLMENT — and updated by nothing. Every date in that column was the day the
/// till was created.
///
/// ⚠ These pin the THROTTLE ARITHMETIC, which is the part with a number in it. The write itself
/// lives in `HeartbeatController`; what must not drift is how often it fires.
/// </summary>
public class DeviceLastSeenThrottleTests
{
    /// <summary>The rule as the controller applies it: write when never written, or when older
    /// than the window.</summary>
    private static bool ShouldPersist(DateTime? lastSeen, DateTime now) =>
        lastSeen is null || now - lastSeen.Value >= TillPresence.PersistEvery;

    /// <summary>⚠ A device nobody has ever heard from must be written on its FIRST beat — otherwise
    /// a freshly enrolled till reads "never" until five minutes have passed, which looks exactly
    /// like the bug this replaces.</summary>
    [Fact]
    public void A_device_never_seen_is_written_immediately() =>
        Assert.True(ShouldPersist(null, new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc)));

    /// <summary>
    /// ⚠⚠ THE POINT OF THE THROTTLE. A fleet beating every 60 seconds must not become a MySQL write
    /// per till per minute for ever — that is precisely the cost `TillPresence` was built in-process
    /// to avoid, and re-introducing it through the back door would make the fix worse than the bug.
    /// </summary>
    [Fact]
    public void A_beat_a_minute_after_the_last_write_does_NOT_write_again()
    {
        var at = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(ShouldPersist(at, at.AddMinutes(1)));
        Assert.False(ShouldPersist(at, at.AddMinutes(4).AddSeconds(59)));
    }

    /// <summary>⚠ And it MUST write once the window has passed, or the column freezes at the first
    /// beat and we are back to a confident, wrong date.</summary>
    [Fact]
    public void A_beat_past_the_window_writes_again()
    {
        var at = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(ShouldPersist(at, at.AddMinutes(5)));
        Assert.True(ShouldPersist(at, at.AddHours(3)));
    }

    /// <summary>
    /// ⚠ Five minutes is not arbitrary: it is the same figure as `StaleWindow`, so a till that has
    /// gone quiet long enough to be called **Offline** is exactly a till whose last-seen is already
    /// on disk. If someone widens one they should think about the other.
    /// </summary>
    [Fact]
    public void The_persist_window_matches_the_offline_window() =>
        Assert.Equal(TillPresence.StaleWindow, TillPresence.PersistEvery);
}
