using System;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Frontend.AppClient.Services.Connectivity;

namespace Plutus.Frontend.AppClient.Services.Storage
{
    /// <summary>What one catalogue sync did, in terms a screen can put in front of a person.</summary>
    /// <param name="Delivered">False = it could not run, or could not reach the server. ⚠ NOT the
    /// same as "nothing had changed" — a caller that conflates them tells an operator the catalogue
    /// is up to date when it has in fact never arrived.</param>
    public sealed record CatalogueSyncOutcome(bool Delivered, int ItemsApplied, int TotalItems, string Message);

    /// <summary>
    /// WP5's missing half: the thing that actually CALLS the catalogue feed.
    ///
    /// ⚠ Every piece of this existed and was tested before today, and none of it ran. `SyncClient`
    /// pages the feed, `TillStore` implements `ISyncStore` and applies each page atomically, the
    /// backend serves `/api/v1/catalogue/changes` — and no line of the app invoked any of it. So a
    /// till that was enrolled, authenticated, connected and healthy had an empty catalogue, and
    /// searching it found nothing. Tested components are not a working feature until something
    /// calls them, and nothing here would have failed a test.
    ///
    /// ⚠ NEVER BLOCKS SELLING, on the same rule as <see cref="SyncClient"/> and
    /// <c>NoticesClient</c>: failures are swallowed and reported in the result. A till whose
    /// catalogue is stale can still sell what it already knows about; a till that refuses to open
    /// because a download failed can sell nothing at all.
    /// </summary>
    public static class CatalogueSyncService
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);

        /// <summary>
        /// Pull whatever has changed since this till's cursor, and apply it.
        ///
        /// ⚠ Re-entrant callers are DROPPED, not queued. A first sync of a 20k catalogue takes
        /// several pages; a sign-in and a timer tick landing together would otherwise run two
        /// pagers over one cursor and interleave their writes.
        /// </summary>
        public static async Task<CatalogueSyncOutcome> SyncAsync(CancellationToken ct = default)
        {
            if (!await Gate.WaitAsync(0, ct).ConfigureAwait(false))
                return new CatalogueSyncOutcome(false, 0, 0, "A catalogue sync is already running.");

            try
            {
                var credentials = await SecureDeviceCredentialStore.LoadAsync().ConfigureAwait(false);
                if (credentials?.DeviceId is not Guid)
                    return new CatalogueSyncOutcome(false, 0, 0, "This till isn't connected to Plutus yet.");

                var http = PlutusHttp.TryFor(new ViewModels.Settings().ServerUrlSetting);
                if (http is null)
                    return new CatalogueSyncOutcome(false, 0, 0, "The server address doesn't look right.");

                var bootstrap = new PlutusApiClient(http);
                var api = new PlutusApiClient(http, new DeviceTokenProvider(bootstrap, credentials));

                var outcome = await TillStoreAccess.UseAsync(
                    store => new SyncClient(api, store).SyncCatalogueAsync(ct), ct).ConfigureAwait(false);

                var total = await TillStoreAccess.UseAsync(s => s.CatalogueCountAsync(ct), ct).ConfigureAwait(false);

                if (!outcome.Succeeded)
                    return new CatalogueSyncOutcome(false, 0, total,
                        $"Couldn't read the catalogue: {outcome.Error}. This till still has {total} item(s).");

                return new CatalogueSyncOutcome(true, outcome.ItemsApplied, total,
                    outcome.ItemsApplied == 0
                        ? $"Already up to date — {total} item(s) on this till."
                        : $"Updated {outcome.ItemsApplied} item(s); {total} on this till.");
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("CatalogueSyncService.SyncAsync", ex);
                return new CatalogueSyncOutcome(false, 0, 0, "Couldn't sync the catalogue. See the Plutus tab's log.");
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>
        /// Kick a sync off without waiting for it.
        ///
        /// ⚠ For sign-in. A first sync pages an entire catalogue, and making somebody stand at a
        /// counter watching a progress bar before they can serve anyone is the opposite of what an
        /// offline-first till is for. Failures land in the crash log, not in front of a customer.
        /// </summary>
        public static void SyncInBackground()
        {
            _ = Task.Run(async () =>
            {
                try { await SyncAsync().ConfigureAwait(false); }
                catch (Exception ex) { Analytics.CrashLog.Write("CatalogueSyncService.SyncInBackground", ex); }
            });
        }
    }
}
