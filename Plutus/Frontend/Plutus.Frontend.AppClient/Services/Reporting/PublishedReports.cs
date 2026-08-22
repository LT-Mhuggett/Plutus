using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Plutus.Frontend.AppClient.Services.Reporting
{
    /// <summary>
    /// Which reports the portal has published to THIS till — ruling 5b(a), 2026-08-19.
    ///
    /// ⚠⚠ MATT: *"Portal shows which reports a till can show."* `ReportCatalogue` is now the SUPERSET;
    /// this decides which of it the operator is offered.
    ///
    /// ⚠⚠ **THE PUBLISH DECIDES THE MENU, THE PERMISSION DECIDES THE DOOR.** This class knows nothing
    /// about permissions and must not learn: `ReportsViewModel` applies both, and a report that is
    /// published but unreadable is not listed at all — never listed-and-refused, because a greyed row
    /// leaks what other roles can see.
    ///
    /// ⚠⚠ **THREE FALLBACKS, IN ORDER, AND THE LAST ONE IS "EVERYTHING".** A till that loses its Reports
    /// tab because the network blinked is worse than one showing a report an owner meant to hide:
    ///
    ///   1. what the server said just now;
    ///   2. the last-known-good cached in <c>Preferences</c> — survives a restart and an offline day;
    ///   3. the full catalogue, when this till has never once had an answer.
    ///
    /// ⚠ Which is why <see cref="Keys"/> is never empty *because of a failure*. It IS empty when a shop
    /// has deliberately published nothing — the cache stores that as a real, empty list, and the two
    /// states are distinguished by whether the key exists at all, exactly as the server distinguishes
    /// "no row" from "an empty row".
    /// </summary>
    internal static class PublishedReports
    {
        /// <summary>⚠ Versioned so a future change of shape cannot be read as a valid old value.</summary>
        private const string CacheKey = "plutus.reports.published.v1";

        private static readonly SemaphoreSlim Gate = new(1, 1);

        /// <summary>
        /// The reports this till may offer, in catalogue order.
        ///
        /// ⚠ Reads the CACHE only — never the network. It is called from viewmodel constructors and from
        /// the UI thread, and a screen must never block on a server to draw a menu.
        /// </summary>
        internal static IReadOnlyList<string> Keys
        {
            get
            {
                var cached = Load();

                // ⚠ NULL = never had an answer → every report. Not "none": see the class remarks.
                return SharedKernel.ReportCatalogue.PublishedOr(cached);
            }
        }

        /// <summary>
        /// Ask the server and update the cache. ⚠ Returns true when the answer CHANGED, so a screen knows
        /// whether it needs to rebuild its menu rather than rebuilding it on every tick.
        ///
        /// ⚠ Never throws. It runs on the settings cadence and from `OnAppearing`; an exception on either
        /// would be an `async void` escape, which is how this app has been killed before.
        /// </summary>
        internal static async Task<bool> RefreshAsync(CancellationToken ct = default)
        {
            // ⚠⚠ A DEADLINE, BECAUSE THIS GATE IS HELD ACROSS A NETWORK CALL. The body below
            // awaits the API, so a request that never returns holds this semaphore for ever and
            // every later refresh queues behind it in silence — the published-report list simply stops
            // updating, with no error anywhere. That is the `TillStoreAccess` fault in a second
            // place, and the convention it established is a deadline on every gate.
            //
            // ⚠ 30s, matching `TillStoreAccess.UseAsync`. One number, so nobody has to remember
            // which gate waits how long.
            // ⚠ AND IT RETURNS FALSE RATHER THAN THROWING — unlike the store gate, and
            // deliberately: this method runs on the settings cadence and its contract is NEVER
            // THROWS. "No change" is already how it reports being offline, and a timeout is the
            // same answer for the caller.
            if (!await Gate.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false))
            {
                Analytics.CrashLog.Write("PublishedReports.RefreshAsync(timeout)", new TimeoutException(
                    "The refresh gate did not become free within 30 seconds; this pass was skipped."));
                return false;
            }
            try
            {
                var api = await Connectivity.PlutusApi.GetAsync(ct).ConfigureAwait(false);
                if (api is null) return false;   // ⚠ Not connected is not a change.

                var credentials = await Connectivity.SecureDeviceCredentialStore.LoadAsync().ConfigureAwait(false);

                var answer = await api.GetPublishedReportsAsync(credentials?.TillId, ct).ConfigureAwait(false);

                // ⚠⚠ NULL IS "COULD NOT ASK", NOT "NOTHING PUBLISHED". Caching an empty list here would
                // empty the Reports tab on the first failed request and keep it empty offline.
                if (answer?.Keys is null) return false;

                var incoming = SharedKernel.ReportCatalogue.PublishedOr(answer.Keys);
                var current = Load();

                var changed = current is null || !current.SequenceEqual(incoming, StringComparer.Ordinal);
                if (changed) Save(incoming);

                return changed;
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("PublishedReports.Refresh", ex);
                return false;
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>
        /// The cached list, or <see langword="null"/> when this till has never had an answer.
        ///
        /// ⚠ A stored EMPTY list is returned as empty, not as null — "the owner published nothing" is a
        /// real state and must survive a restart.
        /// </summary>
        private static IReadOnlyList<string> Load()
        {
            try
            {
                var json = Microsoft.Maui.Storage.Preferences.Get(CacheKey, null);
                if (string.IsNullOrWhiteSpace(json)) return null;

                return JsonSerializer.Deserialize<List<string>>(json);
            }
            catch (Exception ex)
            {
                // ⚠ A corrupt cache reads as "never asked", which shows every report. Failing towards
                // MORE reports is the right direction for a menu; failing towards none is a broken till.
                Analytics.CrashLog.Write("PublishedReports.Load", ex);
                return null;
            }
        }

        private static void Save(IReadOnlyList<string> keys)
        {
            try
            {
                Microsoft.Maui.Storage.Preferences.Set(CacheKey, JsonSerializer.Serialize(keys));
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("PublishedReports.Save", ex);
            }
        }
    }
}
