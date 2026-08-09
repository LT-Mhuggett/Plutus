using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Maui.Storage;
using Plutus.Client.Storage;

namespace Plutus.Frontend.AppClient.Services.Storage
{
    /// <summary>
    /// The one place this app opens the v2 local store.
    ///
    /// ⚠ WHY A SINGLE OWNER. Binding default 9 (2026-08-09, "no bridge to the legacy database")
    /// moves every screen onto <see cref="TillStore"/> one at a time. If each screen opened its own
    /// <see cref="TillDbContext"/> the way the legacy code opens its own
    /// <c>Helpers.Database.Database</c> — 37 call sites across 21 files — the port would reproduce
    /// the exact shape of the problem it exists to remove, on a newer schema.
    ///
    /// ⚠ SQLite AND CONCURRENCY. One context, opened once, shared. EF Core contexts are NOT
    /// thread-safe, so every caller goes through <see cref="UseAsync"/>, which serialises access
    /// behind a semaphore. A till's screens are not hot paths — a checkout is one transaction, a
    /// search is one query — and correctness is worth more than parallelism here: two overlapping
    /// writes to the outbox is a lost or duplicated sale.
    ///
    /// ⚠ The v2 file sits BESIDE the legacy one rather than replacing it. The legacy file is the
    /// cutover archive's input (binding default 3: archive, never delete), and during the port both
    /// exist — the legacy one shrinking in relevance as screens move.
    /// </summary>
    public static class TillStoreAccess
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static TillDbContext? _db;
        private static TillStore? _store;

        /// <summary>Where the v2 store lives. Public so the Plutus tab can show it — "where is my
        /// data" is the first question asked when a till behaves oddly.</summary>
        public static string DatabasePath =>
            Path.Combine(FileSystem.AppDataDirectory, "till-v2.db");

        /// <summary>Run something against the store, serialised. Opens and migrates on first use.</summary>
        public static async Task<T> UseAsync<T>(Func<TillStore, Task<T>> work, CancellationToken ct = default)
        {
            if (work is null) throw new ArgumentNullException(nameof(work));

            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_store is null)
                {
                    var options = new DbContextOptionsBuilder<TillDbContext>()
                        .UseSqlite($"Data Source={DatabasePath}")
                        .Options;
                    _db = new TillDbContext(options);
                    await _db.EnsureReadyAsync(ct).ConfigureAwait(false);
                    _store = new TillStore(_db);
                }

                return await work(_store).ConfigureAwait(false);
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>Void-returning convenience for the many callers that only act.</summary>
        public static Task UseAsync(Func<TillStore, Task> work, CancellationToken ct = default) =>
            UseAsync(async store => { await work(store).ConfigureAwait(false); return true; }, ct);

        /// <summary>
        /// Same, but never throws — for anything on a UI path.
        ///
        /// ⚠ A screen that cannot read its own data should show nothing. It must NEVER be able to
        /// take the app down: a null store dereferenced in a viewmodel constructor is exactly how a
        /// correct password came out as "something went wrong signing in" on 2026-08-09.
        /// </summary>
        public static async Task<T?> TryUseAsync<T>(Func<TillStore, Task<T>> work, CancellationToken ct = default)
        {
            try
            {
                return await UseAsync(work, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("TillStoreAccess.TryUseAsync", ex);
                return default;
            }
        }
    }
}
