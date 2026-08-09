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

            await Gate.WaitAsync(ct).ConfigureAwait(false);
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
