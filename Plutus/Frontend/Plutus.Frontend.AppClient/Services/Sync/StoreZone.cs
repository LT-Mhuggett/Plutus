using System;

namespace Plutus.Frontend.AppClient.Services.Sync
{
    /// <summary>
    /// **The shop's timezone, resolved once per change — WP-TZ, 2026-08-22.**
    ///
    /// ⚠⚠ WHY THIS EXISTS RATHER THAN A `FindSystemTimeZoneById` AT EACH CALL SITE: the clock repaints
    /// **once a second**, and resolving a zone by id is a lookup into the platform's tz database. A
    /// per-tick resolve on a machine that stays open for days is work for nothing, and doing it
    /// inside a UI tick is exactly where nobody would ever look for it.
    ///
    /// ⚠ AND IT MAKES THE UNRESOLVABLE CASE HAPPEN ONCE. A stored id this PC does not know — an old
    /// Windows id, a zone retired between releases — falls back to null, which every reader already
    /// treats as "use the device". Retrying that lookup every second would be a thrown-and-caught
    /// exception a second, forever.
    /// </summary>
    internal static class StoreZone
    {
        private static string _id;

        /// <summary>The resolved zone, or null for "this PC's own clock".</summary>
        public static TimeZoneInfo Current { get; private set; }

        static StoreZone()
        {
            // ⚠ Seeded from whatever the cadence already has — the app bar is built after the first
            // beat on a warm start, and waiting for the next one would show the wrong clock for a
            // minute.
            Apply(TillCadence.StoreTimeZoneId);
            TillCadence.StoreTimeZoneChanged += Apply;
        }

        /// <summary>⚠ Idempotent, and cheap when nothing changed — the cadence raises this on every
        /// beat where the value differs, but a caller may also poke it.</summary>
        public static void Apply(string id)
        {
            if (string.Equals(_id, id, StringComparison.Ordinal)) return;

            _id = id;
            Current = SharedKernel.StoreClock.Resolve(id);
        }

        /// <summary>⚠ Touching this runs the static constructor, which is what subscribes to the
        /// cadence. Called from `App` so the subscription exists before any screen asks.</summary>
        public static void Ensure() { }
    }
}
