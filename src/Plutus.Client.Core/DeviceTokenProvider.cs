using System;
using System.Threading;
using System.Threading.Tasks;

namespace Plutus.Client.Core;

/// <summary>Supplies the bearer token for authenticated calls. Abstracted so the pusher and the
/// API client never care whether it came from a device credential, a cache, or a test stub.</summary>
public interface IDeviceTokenProvider
{
    Task<string?> GetAccessTokenAsync(CancellationToken ct = default);
    /// <summary>Drop the cached token so the next call re-mints. Called on a 401.</summary>
    void Invalidate();
}

/// <summary>Where a till keeps its identity. ⚠ The ClientSecret belongs in platform secure
/// storage (DPAPI / MAUI SecureStorage) — <b>never</b> in the SQLite file, which gets copied off
/// machines during support.</summary>
public interface IDeviceCredentialStore
{
    Guid? DeviceId { get; }
    string? ClientSecret { get; }
    void Save(Guid deviceId, string clientSecret);
    void Clear();
}

/// <summary>
/// Mints and caches the device token, refreshing before expiry rather than waiting for a 401.
///
/// The safety margin matters more than it looks: a till mid-checkout must not discover its token
/// expired between committing a sale and pushing it. Refreshing early costs one cheap call an
/// hour; refreshing late costs a retry cycle on every queued sale.
/// </summary>
public sealed class DeviceTokenProvider : IDeviceTokenProvider
{
    /// <summary>Re-mint this long before the server's stated expiry.</summary>
    public static readonly TimeSpan SafetyMargin = TimeSpan.FromMinutes(2);

    private readonly PlutusApiClient _api;
    private readonly IDeviceCredentialStore _credentials;
    private readonly Func<DateTime> _utcNow;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _token;
    private DateTime _expiresAtUtc = DateTime.MinValue;

    public DeviceTokenProvider(PlutusApiClient api, IDeviceCredentialStore credentials, Func<DateTime>? utcNow = null)
    {
        _api = api;
        _credentials = credentials;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (_token != null && _utcNow() < _expiresAtUtc - SafetyMargin) return _token;

        await _gate.WaitAsync(ct);
        try
        {
            // Re-check inside the gate: several queued sales draining at once must mint ONE token,
            // not one each.
            if (_token != null && _utcNow() < _expiresAtUtc - SafetyMargin) return _token;

            var id = _credentials.DeviceId;
            var secret = _credentials.ClientSecret;
            if (id is not Guid deviceId || string.IsNullOrEmpty(secret)) return null; // not enrolled yet

            var result = await _api.GetDeviceTokenAsync(deviceId, secret, ct);
            _token = result.AccessToken;
            _expiresAtUtc = _utcNow().AddSeconds(result.ExpiresInSeconds);
            return _token;
        }
        finally { _gate.Release(); }
    }

    public void Invalidate()
    {
        _token = null;
        _expiresAtUtc = DateTime.MinValue;
    }
}
