using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Plutus.Tenancy
{
    /// <summary>How present a till is, derived from when it last spoke.</summary>
    public enum PresenceState
    {
        /// <summary>Heard from inside 2 minutes — trading normally.</summary>
        Online = 0,
        /// <summary>2–5 minutes. One or two missed beats: a flaky link, not yet a problem.</summary>
        Stale = 1,
        /// <summary>Over 5 minutes, or never heard from. Someone should look.</summary>
        Offline = 2,
    }

    /// <summary>What the fleet list shows for one till.</summary>
    public sealed record TillPresenceEntry(
        Guid DeviceId,
        Guid TillId,
        PresenceState State,
        DateTime LastSeenUtc,
        string? AppVersion,
        int OutboxDepth,
        long? OldestUnsyncedAgeSeconds,
        TimeSpan? ClockSkew);

    /// <summary>
    /// Where "when did this till last speak" lives.
    ///
    /// ⚠ IN PROCESS, NOT MySQL, AND THAT IS THE WHOLE POINT. A fleet of tills beating every 60
    /// seconds is a write per till per minute, forever, of data whose value expires in five
    /// minutes — it would be the busiest write path in the system and the least useful row in the
    /// database. Presence is ephemeral: if the backend restarts, every till re-reports within a
    /// minute and the picture rebuilds itself.
    ///
    /// A single pm2 instance runs this stack, so one dictionary is the whole story. ⚠ If the
    /// backend is ever scaled out, presence becomes per-instance and the portal shows whichever
    /// instance answered — at which point this needs a shared store, and there is deliberately no
    /// Redis in this stack to reach for. Named here so that decision is made on purpose.
    ///
    /// Registered as a SINGLETON (TenancyModule).
    /// </summary>
    public sealed class TillPresence
    {
        /// <summary>Heard from inside this: Online. Two missed beats plus a margin.</summary>
        public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(2);

        /// <summary>Beyond this: Offline. Five missed beats — long enough that it is not a blip.</summary>
        public static readonly TimeSpan StaleWindow = TimeSpan.FromMinutes(5);

        private sealed record Beat(
            Guid TillId, DateTime AtUtc, string? AppVersion,
            int OutboxDepth, long? OldestUnsyncedAgeSeconds, TimeSpan? ClockSkew);

        private readonly ConcurrentDictionary<Guid, Beat> _beats = new();
        private readonly Func<DateTime> _utcNow;

        public TillPresence(Func<DateTime>? utcNow = null) => _utcNow = utcNow ?? (() => DateTime.UtcNow);

        public void Record(
            Guid deviceId, Guid tillId, string? appVersion,
            int outboxDepth, long? oldestUnsyncedAgeSeconds, DateTime deviceClockUtc)
        {
            var now = _utcNow();
            _beats[deviceId] = new Beat(
                tillId, now, appVersion, outboxDepth, oldestUnsyncedAgeSeconds,
                // Measured against the moment we recorded it, so a slow request reads as latency
                // rather than as the till's clock being wrong.
                deviceClockUtc == default ? null : deviceClockUtc - now);
        }

        /// <summary>The state for one device. Never heard from = <see cref="PresenceState.Offline"/>,
        /// which is the honest answer: a till that has never beaten is not online.</summary>
        public PresenceState StateOf(Guid deviceId) =>
            _beats.TryGetValue(deviceId, out var b) ? StateAt(b.AtUtc, _utcNow()) : PresenceState.Offline;

        public TillPresenceEntry? Get(Guid deviceId) =>
            _beats.TryGetValue(deviceId, out var b)
                ? new TillPresenceEntry(deviceId, b.TillId, StateAt(b.AtUtc, _utcNow()), b.AtUtc,
                    b.AppVersion, b.OutboxDepth, b.OldestUnsyncedAgeSeconds, b.ClockSkew)
                : null;

        /// <summary>Everything currently known, newest first.</summary>
        public IReadOnlyList<TillPresenceEntry> All()
        {
            var now = _utcNow();
            return _beats
                .Select(kv => new TillPresenceEntry(
                    kv.Key, kv.Value.TillId, StateAt(kv.Value.AtUtc, now), kv.Value.AtUtc,
                    kv.Value.AppVersion, kv.Value.OutboxDepth, kv.Value.OldestUnsyncedAgeSeconds,
                    kv.Value.ClockSkew))
                .OrderByDescending(e => e.LastSeenUtc)
                .ToList();
        }

        private static PresenceState StateAt(DateTime lastSeenUtc, DateTime now)
        {
            var age = now - lastSeenUtc;
            if (age <= OnlineWindow) return PresenceState.Online;
            return age <= StaleWindow ? PresenceState.Stale : PresenceState.Offline;
        }
    }
}
