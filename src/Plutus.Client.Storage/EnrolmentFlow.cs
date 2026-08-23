using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;

namespace Plutus.Client.Storage;

/// <summary>Why a till cannot enrol yet, in the operator's words.</summary>
public sealed class EnrolmentBlockedException : Exception
{
    public EnrolmentBlockedException(string message) : base(message) { }
}

/// <summary>
/// MAUI retrofit WP4: the first-run flow that turns a standalone till into an enrolled device.
///
/// ⚠ MAUI is NOT same-origin. The web till never needed a server address; a native till does, so
/// first run collects a Server URL alongside the enrolment code.
///
/// ⚠ THE ARCHIVE GATE (binding default §9.3/§9.4). Enrolment REFUSES while an un-archived legacy
/// database is present. Every live till holds real sales and held baskets in its old file; if a
/// till enrols and starts a fresh v2 store without that file being archived first, the shop's
/// history is stranded on a machine that now looks empty — and the translation agent has nothing
/// to ingest. Refusing loudly is the only safe default, because the failure is silent otherwise.
/// </summary>
public sealed class EnrolmentFlow
{
    private readonly TillStore _store;
    private readonly PlutusApiClient _api;
    private readonly IDeviceCredentialStore _credentials;

    public EnrolmentFlow(TillStore store, PlutusApiClient api, IDeviceCredentialStore credentials)
    {
        _store = store;
        _api = api;
        _credentials = credentials;
    }

    /// <summary>Has this till already enrolled? Used to skip first-run on every later launch.</summary>
    public async Task<bool> IsEnrolledAsync(CancellationToken ct = default) =>
        _credentials.DeviceId != null && await _store.GetGuidMetaAsync(MetaKeys.TillId, ct) != null;

    // ⚠⚠ L1, 2026-08-23 — `BlockedReasonAsync` DELETED, and this removes a protection rather than
    // dead code. It refused enrolment on a till still holding an un-archived legacy database, so
    // that a shop migrating off NatApp could not strand its own history by enrolling first.
    //
    // ⚠ IT WAS ALREADY INERT: the one caller passed `null`, so the gate returned immediately and
    // nothing was ever blocked. Matt's call, 2026-08-10, on the basis that no such migration is
    // planned. ⚠ A future one needs this gate AND an on-ramp rebuilt before it is switched back on.

    /// <summary>
    /// Redeem an enrolment code and record the till's identity.
    ///
    /// The ClientSecret goes to <see cref="IDeviceCredentialStore"/> (platform secure storage) and
    /// NEVER into the local database — support copies that file off machines routinely.
    /// </summary>
    /// <remarks>⚠ `legacyDatabasePath` WENT WITH THE ARCHIVE GATE (L1, 2026-08-23). It existed only
    /// to be handed to `BlockedReasonAsync`, and the one production caller passed null.</remarks>
    public async Task<Guid> EnrolAsync(
        string serverUrl, string enrolmentCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
            throw new EnrolmentBlockedException("A server address is needed before this till can enrol.");

        var result = await _api.EnrolAsync(enrolmentCode.Trim(), ct);

        _credentials.Save(result.DeviceId, result.ClientSecret);
        await _store.SetMetaAsync(MetaKeys.ServerUrl, serverUrl.Trim(), ct);
        await _store.SetMetaAsync(MetaKeys.DeviceId, result.DeviceId.ToString("D"), ct);
        await _store.SetMetaAsync(MetaKeys.TillId, result.TillId.ToString("D"), ct);
        await _store.SetMetaAsync(MetaKeys.TenantId, result.TenantId.ToString("D"), ct);
        return result.DeviceId;
    }

    /// <summary>
    /// Learn (and cache) which store this till belongs to, and the legacy BusinessId that seeds
    /// item-id derivation. Called after enrolment and on each start — a till can be MOVED between
    /// stores in the portal, and its receipts, themes and store info must follow it.
    ///
    /// ⚠ businessId comes from the server, never from the tenant id.
    /// </summary>
    public async Task RefreshPlacementAsync(CancellationToken ct = default)
    {
        var tillId = await _store.GetGuidMetaAsync(MetaKeys.TillId, ct);
        if (tillId is not Guid id) return;

        var name = await _api.GetTillNameAsync(id, ct);
        if (name?.StoreId is not int storeId) return;
        await _store.SetMetaAsync(MetaKeys.StoreId, storeId.ToString(), ct);

        var info = await _api.GetStoreInfoAsync(storeId, ct);
        if (info?.BusinessId is Guid businessId && businessId != Guid.Empty)
            await _store.SetMetaAsync(MetaKeys.BusinessId, businessId.ToString("D"), ct);
    }

    /// <summary>Forget this device's credential — after the portal revokes it, or on un-enrol.
    /// Local SALES ARE KEPT: they are money that may not have synced yet, and dropping them
    /// because a credential went away would be the worst possible response.</summary>
    public async Task ForgetDeviceAsync(CancellationToken ct = default)
    {
        _credentials.Clear();

        // ⚠ CLEAR THE PLACEMENT TOO, not just the credential. `EnrolAsync` records DeviceId, TillId
        // and TenantId, and `RefreshPlacementAsync` adds StoreId and BusinessId — so clearing the
        // device id alone left the till still ANSWERING as the till it had just been un-enrolled
        // from: `TillPlacement` kept handing out the old TillId/StoreId, receipts kept the old
        // store's address, and item ids kept deriving from the old BusinessId. A device that has
        // been forgotten must not keep claiming a posting.
        //
        // ⚠ `ServerUrl` DELIBERATELY SURVIVES. It is how the operator reaches the portal to enrol
        // again; wiping it turns "forget this till" into "and now type the address in from memory".
        await _store.SetMetaAsync(MetaKeys.DeviceId, null, ct);
        await _store.SetMetaAsync(MetaKeys.TillId, null, ct);
        await _store.SetMetaAsync(MetaKeys.TenantId, null, ct);
        await _store.SetMetaAsync(MetaKeys.StoreId, null, ct);
        await _store.SetMetaAsync(MetaKeys.BusinessId, null, ct);
    }
}
