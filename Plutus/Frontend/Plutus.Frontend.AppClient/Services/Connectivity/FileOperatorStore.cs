using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using Plutus.Client.Core;
using Plutus.Contracts.Client;

namespace Plutus.Frontend.AppClient.Services.Connectivity
{
    /// <summary>
    /// ⚠⚠ SUPERSEDED 2026-08-17 (step 24) BY <see cref="DbOperatorStore"/>, WHICH IMPORTS FROM THIS.
    /// **No call site constructs this any more** — it survives for exactly one job: seeding the till
    /// database the first time `DbOperatorStore` finds no roster there.
    ///
    /// ⚠ **DO NOT DELETE IT, and do not delete `operators.json`.** A till that upgrades while
    /// **offline** has no other source for its roster, and the roster IS offline sign-in — deleting
    /// this makes the upgrade a lockout for any shop whose broadband is down that morning. Matt,
    /// 2026-08-17: *"Do not drop anything."* It can go once every till in the estate has run 1.72.0
    /// online at least once, and not before.
    ///
    /// ⚠ ITS OLD HEADER CLAIMED A BLOCKER THAT NO LONGER EXISTS, and said so confidently: that
    /// `Plutus.Client.Storage` was on EF Core 9 while this app carried a direct
    /// `Microsoft.EntityFrameworkCore.Sqlite 3.1.17` pin, so referencing it failed restore. **The app
    /// is on 9.0.18, the `Database` project is on 9.0.18 including Proxies, and
    /// `Plutus.Client.Storage` was already a project reference.** The blocker died with the .NET 10
    /// upgrade and the comment outlived it — which is why the plan and this file disagreed about
    /// whether the move was possible.
    ///
    /// The till's operator roster, on disk as JSON.
    ///
    /// ⚠ None of the RULES live here — verification, staleness horizons and permission gating are all
    /// in <c>Plutus.Client.Core</c> / <c>SharedKernel</c>, which is why moving the storage changed
    /// where bytes sit and nothing else.
    ///
    /// ⚠ What is stored is PBKDF2 HASHES, not passwords — but they are the operators' platform
    /// credentials, so the file is exactly why <c>OfflineCredentials</c> bounds how long they are
    /// trusted. It sits in the app's private data directory alongside the till database, which is
    /// the same protection the legacy employee table already had.
    /// </summary>
    internal sealed class FileOperatorStore : IOperatorStore   // ⚠ import source only — see the header
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private readonly SemaphoreSlim _gate = new(1, 1);

        /// <summary>
        /// ⚠⚠ EVERY GATE IN THIS APP WAITS WITH A DEADLINE — see `TillGateDeadlineTests`. These
        /// three methods used to `WaitAsync(ct)`, which is unbounded: a `CancellationToken` carries
        /// the CALLER'S cancellation, not a timeout, and on the hang path nothing cancels it.
        ///
        /// ⚠ THIS GATE GUARDS THE OPERATOR ROSTER, so a hang here means nobody can sign in — and
        /// with no exception and nothing logged, the till would simply stop accepting staff.
        ///
        /// ⚠ 30 seconds, matching `TillStoreAccess.UseAsync`. This gate is held only across a small
        /// local file read or write, so half a minute is not contention, it is a hang.
        /// </summary>
        private async Task<bool> EnterAsync(string who, CancellationToken ct)
        {
            if (await _gate.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false)) return true;

            Analytics.CrashLog.Write($"FileOperatorStore.{who}(timeout)", new TimeoutException(
                "The operator-roster file gate did not become free within 30 seconds."));
            return false;
        }

        private static string Path => System.IO.Path.Combine(FileSystem.AppDataDirectory, "operators.json");

        public async Task<TillOperatorsResult?> LoadAsync(CancellationToken ct = default)
        {
            // ⚠ Null is this method's existing "no roster here" answer — every caller handles it.
            if (!await EnterAsync(nameof(LoadAsync), ct).ConfigureAwait(false)) return null;
            try
            {
                if (!File.Exists(Path)) return null;
                await using var stream = File.OpenRead(Path);
                return await JsonSerializer.DeserializeAsync<TillOperatorsResult>(stream, Json, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // ⚠ A corrupt cache is "no operators", not a crash. That state has a screen and a
                // message; an exception on the login path has neither.
                Analytics.CrashLog.Write("FileOperatorStore.Load", ex);
                return null;
            }
            finally { _gate.Release(); }
        }

        public async Task SaveAsync(TillOperatorsResult roster, CancellationToken ct = default)
        {
            // ⚠ A DROPPED SAVE, NOT A CRASH. This runs from the sync cadence; throwing on a
            // background tick is how this app has been killed before. The next sync writes again.
            if (!await EnterAsync(nameof(SaveAsync), ct).ConfigureAwait(false)) return;
            try
            {
                Directory.CreateDirectory(FileSystem.AppDataDirectory);

                // ⚠ Write-then-move. A half-written roster is a till nobody can sign in to, and a
                // power cut mid-write is exactly the moment that would happen.
                var temp = Path + ".tmp";
                await using (var stream = File.Create(temp))
                    await JsonSerializer.SerializeAsync(stream, roster, Json, ct);
                File.Move(temp, Path, overwrite: true);
            }
            finally { _gate.Release(); }
        }

        public async Task ClearAsync(CancellationToken ct = default)
        {
            // ⚠ Un-enrol calls this. A missed clear leaves a stale roster on disk, which the
            // staleness tier already refuses to trust — worse than a crash mid-un-enrol it is not.
            if (!await EnterAsync(nameof(ClearAsync), ct).ConfigureAwait(false)) return;
            try { if (File.Exists(Path)) File.Delete(Path); }
            catch (Exception ex) { Analytics.CrashLog.Write("FileOperatorStore.Clear", ex); }
            finally { _gate.Release(); }
        }
    }
}
