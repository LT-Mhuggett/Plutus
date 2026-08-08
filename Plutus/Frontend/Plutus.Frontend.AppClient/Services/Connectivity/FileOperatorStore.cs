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
    /// The till's operator roster, on disk as JSON.
    ///
    /// ⚠ WHY A FILE AND NOT THE LOCAL DATABASE. The proper home is
    /// <c>Plutus.Client.Storage.LocalOperator</c> — but that project is on EF Core 9 while this app
    /// still carries a direct <c>Microsoft.EntityFrameworkCore.Sqlite 3.1.17</c> pin and the legacy
    /// <c>Database</c> project. Referencing it fails <c>restore</c> outright (NU1605 downgrade,
    /// verified with a probe project), and the obvious fix — dropping the direct pin — silently
    /// swaps the LIVE till database onto EF 9 with a stranded EF 3.1 Proxies package: a runtime
    /// TypeLoadException in the code a shop is trading on. That unwiring is WP2's cutover, not
    /// something to slip in behind a login screen.
    ///
    /// So: a file, until the cutover moves it. ⚠ None of the RULES live here — verification,
    /// staleness horizons and permission gating are all in <c>Plutus.Client.Core</c> /
    /// <c>SharedKernel</c>, so moving the storage later changes where bytes sit and nothing else.
    ///
    /// ⚠ What is stored is PBKDF2 HASHES, not passwords — but they are the operators' platform
    /// credentials, so the file is exactly why <c>OfflineCredentials</c> bounds how long they are
    /// trusted. It sits in the app's private data directory alongside the till database, which is
    /// the same protection the legacy employee table already had.
    /// </summary>
    internal sealed class FileOperatorStore : IOperatorStore
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private readonly SemaphoreSlim _gate = new(1, 1);

        private static string Path => System.IO.Path.Combine(FileSystem.AppDataDirectory, "operators.json");

        public async Task<TillOperatorsResult?> LoadAsync(CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct);
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
            await _gate.WaitAsync(ct);
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
            await _gate.WaitAsync(ct);
            try { if (File.Exists(Path)) File.Delete(Path); }
            catch (Exception ex) { Analytics.CrashLog.Write("FileOperatorStore.Clear", ex); }
            finally { _gate.Release(); }
        }
    }
}
