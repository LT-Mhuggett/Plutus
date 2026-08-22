using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Client.Storage;
using Plutus.SharedKernel;

namespace Plutus.Frontend.AppClient.Services.Connectivity
{
    /// <summary>
    /// Where this till keeps the device-local verifiers minted by an online sign-in — **step 28**.
    ///
    /// ⚠⚠ IN ITS OWN META KEY, NOT IN THE ROSTER. `MetaKeys.OperatorRoster` is a CACHE, replaced
    /// wholesale on every sync — anything stored alongside it would be destroyed by a routine
    /// refresh, silently re-imposing "connect once" on the whole shop mid-shift with nothing on
    /// screen to explain it. A verifier is earned by an online sign-in and must outlive every pull.
    ///
    /// ⚠⚠ AND IT IS THE ONLY THING ON THIS TILL WORTH STEALING THAT WE CHOSE TO PUT THERE. That is
    /// the trade step 28 makes deliberately: the platform hash the roster ships is an operator's
    /// PLATFORM password (it works on the web till and the portal); this is a local artefact at
    /// SHA-256/600,000 that is useless on any other machine. See `SharedKernel.DeviceVerifier`.
    ///
    /// ⚠ ONE JSON MAP, not a row per user. A shop has a handful of staff, the whole thing is read on
    /// every sign-in, and a table would need a migration for a value that is pure cache-with-consent.
    /// </summary>
    internal sealed class DbDeviceVerifierStore : IDeviceVerifierStore
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public async Task<DeviceVerifier.Record> GetAsync(Guid userId, CancellationToken ct = default)
        {
            var all = await ReadAsync(ct).ConfigureAwait(false);
            return all.TryGetValue(userId.ToString("D"), out var r) ? r : null;
        }

        /// <summary>
        /// ⚠ OVERWRITES. A password changed on the platform and proved online must REPLACE what this
        /// till holds — otherwise the old password keeps working offline for ever, which is the
        /// opposite of what a password change is for.
        /// </summary>
        public async Task SaveAsync(DeviceVerifier.Record record, CancellationToken ct = default)
        {
            if (record is null) return;

            var all = await ReadAsync(ct).ConfigureAwait(false);
            all[record.UserId.ToString("D")] = record;
            await WriteAsync(all, ct).ConfigureAwait(false);
        }

        public async Task ForgetAsync(Guid userId, CancellationToken ct = default)
        {
            var all = await ReadAsync(ct).ConfigureAwait(false);
            if (!all.Remove(userId.ToString("D"))) return;
            await WriteAsync(all, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// ⚠⚠ A CORRUPT OR UNREADABLE STORE IS "NO VERIFIERS", NEVER A THROW. This runs on the login
        /// path: an exception here would be a till nobody can sign into, where the honest answer is
        /// "connect once" — which is a state the login screen already has words for.
        /// </summary>
        private static async Task<Dictionary<string, DeviceVerifier.Record>> ReadAsync(CancellationToken ct)
        {
            try
            {
                var stored = await Storage.TillStoreAccess.TryUseAsync(
                    s => s.GetMetaAsync(MetaKeys.DeviceVerifiers, ct), ct).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(stored)) return new Dictionary<string, DeviceVerifier.Record>();

                return JsonSerializer.Deserialize<Dictionary<string, DeviceVerifier.Record>>(stored, Json)
                       ?? new Dictionary<string, DeviceVerifier.Record>();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Analytics.CrashLog.Write("DbDeviceVerifierStore.Read", ex);
                return new Dictionary<string, DeviceVerifier.Record>();
            }
        }

        /// <summary>⚠ A failed write costs a reconnection next time and nothing else — it must never
        /// fail the sign-in that has already been proved.</summary>
        private static async Task WriteAsync(Dictionary<string, DeviceVerifier.Record> all, CancellationToken ct)
        {
            try
            {
                var json = JsonSerializer.Serialize(all, Json);
                await Storage.TillStoreAccess.TryUseAsync(async s =>
                {
                    await s.SetMetaAsync(MetaKeys.DeviceVerifiers, json, ct).ConfigureAwait(false);
                    return true;
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Analytics.CrashLog.Write("DbDeviceVerifierStore.Write", ex);
            }
        }
    }
}
