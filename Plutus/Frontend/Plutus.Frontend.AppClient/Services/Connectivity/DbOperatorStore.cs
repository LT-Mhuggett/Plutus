using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Client.Storage;
using Plutus.Contracts.Client;

namespace Plutus.Frontend.AppClient.Services.Connectivity
{
    /// <summary>
    /// The till's operator roster, in the TILL DATABASE (step 24's last item).
    ///
    /// ⚠⚠ WHY THIS REPLACES <see cref="FileOperatorStore"/>. Two roster stores is drift by
    /// construction — one of them is always the stale one, and which is authoritative depends on
    /// which code path ran last. The file's own header explained why it existed: `Plutus.Client.Storage`
    /// was on EF Core 9 while this app carried a direct EF **3.1.17** pin, so referencing it failed
    /// restore. ⚠ **That is no longer true and the header was stale** — the app is on
    /// `Microsoft.EntityFrameworkCore.Sqlite 9.0.18`, the `Database` project is on 9.0.18 including
    /// Proxies, and `Plutus.Client.Storage` is **already** a project reference. The blocker died with
    /// the .NET 10 upgrade.
    ///
    /// ⚠⚠ STORED AS THE WHOLE WIRE ENVELOPE, in `MetaKeys.OperatorRoster`, NOT as rows in
    /// `TillDbContext.Operators`. The plan named `Operators`, and that target is wrong: `LocalOperator`
    /// has **no Email** column — and `OperatorLogin` matches on email first, so relational storage
    /// would break the normal way staff sign in — and there is nowhere for the roster-level `AsOfUtc`
    /// that `OfflineCredentials.Assess` measures against. ⚠ `AsOfUtc` is the SERVER's clock by
    /// contract, so replacing it with a locally-written timestamp would turn a server-anchored
    /// staleness horizon into a client-anchored one, silently. Matt, 2026-08-17: *"Do not drop
    /// anything."* Nothing is dropped here.
    ///
    /// ⚠⚠ IT IMPORTS THE JSON FILE ONCE, and that is the load-bearing part. A till that upgrades while
    /// **offline** would otherwise wake with an empty roster and **nobody able to sign in** — and no
    /// way to fetch one, because fetching needs the network it hasn't got. The cached roster is
    /// precisely what offline sign-in is.
    ///
    /// ⚠ The old file is **read, not deleted**. Leaving it costs a few KB and means a downgrade to an
    /// earlier build still finds a roster; deleting it would make the upgrade one-way for no gain.
    /// </summary>
    internal sealed class DbOperatorStore : IOperatorStore
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        /// <summary>⚠ The legacy source, used only to seed the first read. Never written to again.</summary>
        private readonly FileOperatorStore _legacy = new();

        public async Task<TillOperatorsResult?> LoadAsync(CancellationToken ct = default)
        {
            try
            {
                var stored = await Storage.TillStoreAccess.TryUseAsync(
                    s => s.GetMetaAsync(MetaKeys.OperatorRoster, ct), ct).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(stored))
                    return JsonSerializer.Deserialize<TillOperatorsResult>(stored, Json);

                // ⚠⚠ THE ONE-TIME IMPORT. Nothing in the till database yet — so if the old file has a
                // roster, adopt it and write it across. This is what stops an OFFLINE upgrade locking
                // a shop out of its own till.
                var carried = await _legacy.LoadAsync(ct).ConfigureAwait(false);
                if (carried is null) return null;

                await SaveAsync(carried, ct).ConfigureAwait(false);
                return carried;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // ⚠ A corrupt roster is "no operators", not a crash — that state has a screen and a
                // message; an exception on the login path has neither. Same contract as the file store.
                Analytics.CrashLog.Write("DbOperatorStore.Load", ex);
                return null;
            }
        }

        public async Task SaveAsync(TillOperatorsResult roster, CancellationToken ct = default)
        {
            if (roster is null) return;

            try
            {
                var json = JsonSerializer.Serialize(roster, Json);

                await Storage.TillStoreAccess.TryUseAsync(async s =>
                {
                    await s.SetMetaAsync(MetaKeys.OperatorRoster, json, ct).ConfigureAwait(false);
                    return true;
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // ⚠ A roster that could not be cached must not break the sign-in that just succeeded.
                // The operator is in; the next beat will try again.
                Analytics.CrashLog.Write("DbOperatorStore.Save", ex);
            }
        }

        public async Task ClearAsync(CancellationToken ct = default)
        {
            try
            {
                await Storage.TillStoreAccess.TryUseAsync(async s =>
                {
                    await s.SetMetaAsync(MetaKeys.OperatorRoster, string.Empty, ct).ConfigureAwait(false);
                    return true;
                }, ct).ConfigureAwait(false);

                // ⚠⚠ AND THE LEGACY FILE TOO. `Clear` is what un-enrolment and "forget this till" call,
                // and leaving credentials behind in the old file would let the next `LoadAsync` import
                // the roster of a till this machine is no longer part of.
                await _legacy.ClearAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Analytics.CrashLog.Write("DbOperatorStore.Clear", ex);
            }
        }
    }
}
