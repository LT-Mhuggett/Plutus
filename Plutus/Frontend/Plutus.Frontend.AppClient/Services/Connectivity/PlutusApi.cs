using System;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;

namespace Plutus.Frontend.AppClient.Services.Connectivity
{
    /// <summary>
    /// The one authenticated API client this app uses.
    ///
    /// ⚠ ONE `DeviceTokenProvider`, AND THIS IS A RATE-LIMIT BUG, NOT TIDINESS. `POST
    /// /api/v1/tokens/device` is limited to **5 per minute per IP**. A provider caches the token it
    /// mints — but each INSTANCE has its own cache, so eight services each holding their own
    /// provider meant eight separate mints. One cadence tick alone did four (heartbeat, outbox
    /// drain, catalogue sync, gateway surcharge), so a healthy till exceeded the limit inside its
    /// first minute and every call after that failed with
    /// `EnrolmentFailedException: Could not get a device token (429)` — which reads, from the till,
    /// exactly like being revoked.
    ///
    /// ⚠ Two tills behind one shop's router do this to each other as well, so the limit is per-IP
    /// and not per-till: the fix has to be "mint rarely", not "mint on a bigger allowance".
    ///
    /// ⚠ Rebuilt when the SERVER URL or the CREDENTIAL changes — re-enrolment issues a new device
    /// id and secret, and a cached client would keep presenting the retired one.
    /// </summary>
    internal static class PlutusApi
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static PlutusApiClient _client;
        private static DeviceTokenProvider _tokens;
        private static string _builtForUrl;
        private static Guid? _builtForDevice;

        /// <summary>
        /// The client AND the token provider behind it — the outbox pusher needs the provider so it
        /// can invalidate on a 401 and re-mint ONCE mid-drain.
        ///
        /// ⚠ The provider must be the SAME one the client authenticates with, or invalidating it
        /// clears a cache nobody is reading and the drain re-tries with the same dead token.
        /// </summary>
        internal static async Task<(PlutusApiClient Api, DeviceTokenProvider Tokens)> GetWithTokensAsync(
            CancellationToken ct = default)
        {
            var api = await GetAsync(ct).ConfigureAwait(false);
            return (api, api is null ? null : _tokens);
        }

        /// <summary>
        /// The shared client, or null when this till has no credential or no usable server address.
        /// ⚠ Null is an ANSWER — "not connected" — and every caller already handles it. It must not
        /// throw: most callers are on a UI path or a background tick.
        /// </summary>
        internal static async Task<PlutusApiClient> GetAsync(CancellationToken ct = default)
        {
            var credentials = await SecureDeviceCredentialStore.LoadAsync().ConfigureAwait(false);
            if (credentials?.DeviceId is not Guid deviceId) return null;

            var url = new ViewModels.Settings().ServerUrlSetting;

            // ⚠⚠ A DEADLINE — the convention `TillStoreAccess.UseAsync` set, applied to every gate
            // in the till. This one only builds an HttpClient, so it should never be slow; that is
            // exactly why an un-deadlined wait here would be so hard to diagnose if it ever were.
            // A stuck gate means EVERY api call afterwards waits for ever, and the till just stops
            // reaching the server with nothing logged.
            // ⚠ Returns null, which is this method's existing "not connected" answer — every caller
            // already handles it, so a timeout degrades to offline rather than to an exception on a
            // background tick.
            if (!await Gate.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false))
            {
                Analytics.CrashLog.Write("PlutusApi.GetAsync(timeout)", new TimeoutException(
                    "The API-client gate did not become free within 30 seconds."));
                return null;
            }
            try
            {
                if (_client is not null && _builtForUrl == url && _builtForDevice == deviceId)
                    return _client;

                var http = PlutusHttp.TryFor(url);
                if (http is null) return null;

                var bootstrap = new PlutusApiClient(http);
                _tokens = new DeviceTokenProvider(bootstrap, credentials);
                _client = new PlutusApiClient(http, _tokens);
                _builtForUrl = url;
                _builtForDevice = deviceId;
                return _client;
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>
        /// The same server, authorised as the SIGNED-IN OPERATOR rather than as the till.
        ///
        /// ⚠ FOR "MAY THIS PERSON DO THIS" CALLS ONLY. `perm:*` policies resolve from RBAC by the
        /// token's userId, and a DEVICE token has no userId — so every operator-permission endpoint
        /// answers 403 to the device-authorised client. That is why the cross-till refund lookup
        /// has never worked: `GET /api/v1/sales/{saleId}` is gated
        /// `portal.financials.view,pos.reports.view,pos.refund`, and `ReturnLookup` swallowed the
        /// 403 and fell back to this till's own record.
        ///
        /// ⚠ NEVER use it for sales ingest, the heartbeat, the catalogue feed or enrolment. Those
        /// are the till speaking as itself and must keep working overnight with nobody signed in —
        /// and the server derives the till id from the device token rather than trusting a body.
        ///
        /// ⚠ Returns null when nobody is signed in or the session has lapsed. That is an ANSWER, not
        /// an error: the caller falls back to what this till knows locally.
        ///
        /// ⚠ NOT CACHED, deliberately. The token changes with every sign-in and expires on its own;
        /// caching the CLIENT would pin whichever operator happened to be first. Building one is a
        /// couple of allocations over the shared `HttpClient` — the expensive thing this file exists
        /// to avoid is minting DEVICE tokens, and this path mints nothing at all.
        /// </summary>
        internal static async Task<PlutusApiClient> GetOperatorAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(OperatorSession.Token)) return null;

            var http = PlutusHttp.TryFor(new ViewModels.Settings().ServerUrlSetting);
            return http is null ? null : new PlutusApiClient(http, OperatorTokenProvider.Instance);
        }

        /// <summary>Drop the cached client — after re-enrolment, or when the server address
        /// changes. The next caller builds a fresh one against the new credential.</summary>
        internal static void Reset()
        {
            Gate.Wait();
            try { _client = null; _tokens = null; _builtForUrl = null; _builtForDevice = null; }
            finally { Gate.Release(); }
        }
    }
}
