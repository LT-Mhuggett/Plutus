using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Contracts.Client;

namespace Plutus.Client.Core;

// ─────────────────────────────────────────────────────────────────────────────
// "Am I connected?" — asked at the login screen, answered honestly.
//
// THE PROBLEM THIS SOLVES: "offline" is three different faults wearing one word, and the operator
// standing at the till is the person who has to act on the difference:
//
//   · no network          → their problem. Check the cable, the wifi, the 4G dongle.
//   · no server           → our problem, or their ISP's. Nothing they do at the till will help.
//   · till not accepted   → a manager's problem. The device was revoked or never enrolled; the
//                           network is fine and rebooting it forever will not help.
//
// A single red "offline" badge sends people to reboot routers when their device credential was
// revoked in the portal. So the till asks two questions in order — reach the server at all
// (anonymous ping, no database), then prove this till is still accepted (device token, which does
// hit the database) — and reports which one failed.
//
// ⚠ This lives in Client.Core, NOT in the MAUI project, because it is a RULE and rules are shared
// (till-design.md D2/D3). The web till, the MAUI till and any future macOS/Linux till must give
// the same answer to the same question; a copy per platform is a copy that drifts.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>What a till's link to the platform actually is right now.</summary>
public enum TillConnection
{
    /// <summary>The OS reports no usable network. Don't blame the server — we never asked it.</summary>
    NoNetwork = 0,

    /// <summary>Network is up; nothing answered at the configured server address. DNS, TLS, a
    /// refused connection and a timeout are all this — the difference matters to support, not to
    /// the sales floor, so it goes in <see cref="ConnectionStatus.Detail"/>.</summary>
    NoServer = 1,

    /// <summary>The backend answered and refused this till's credential — revoked in the portal,
    /// or a secret that no longer matches. ⚠ Not a network fault, and no amount of reconnecting
    /// fixes it.</summary>
    Rejected = 2,

    /// <summary>The backend answered, but this till holds no device credential yet. A first run,
    /// not a fault.</summary>
    NotEnrolled = 3,

    /// <summary>The backend answered and issued this till a device token. Fully online.</summary>
    Online = 4,
}

/// <summary>The result of one probe. <see cref="Summary"/> is for the operator;
/// <see cref="Detail"/> is for whoever they ring afterwards.</summary>
public sealed record ConnectionStatus(
    TillConnection State,
    string Summary,
    string? Detail,
    DateTime CheckedAtUtc,
    TimeSpan RoundTrip,
    TimeSpan? ClockSkew,
    string? DeviceStatus = null)
{
    /// <summary>⚠ A manager has asked for this till back, but nobody has approved it yet. The till
    /// KEEPS TRADING — stopping on the request alone would turn un-enrolment into a way to take a
    /// shop's till down. Worth showing in Settings; never worth blocking a sale over.</summary>
    public bool PendingRemoval => string.Equals(DeviceStatus, "PendingRemoval", StringComparison.OrdinalIgnoreCase);

    /// <summary>Something Plutus-shaped answered. True even when this till was rejected — that is
    /// the point of separating the two questions.</summary>
    public bool ServerReachable => State is TillConnection.Rejected or TillConnection.NotEnrolled or TillConnection.Online;

    /// <summary>Fully online: reachable AND this till is accepted. The only state in which
    /// server-backed features (credit tender, cross-till refunds, reports) may be offered.</summary>
    public bool IsOnline => State == TillConnection.Online;

    /// <summary>⚠ The till's clock disagrees with the server's by enough to matter. Device tokens
    /// carry an expiry and VAT bands are effective-dated, so a drifted till can have a day's
    /// takings judged against the wrong instant.</summary>
    public bool ClockSuspect => ClockSkew is TimeSpan s && s.Duration() > ConnectivityProbe.ClockSkewTolerance;
}

/// <summary>
/// Whether the OS thinks there is a network. Abstracted because the answer is platform-specific
/// (MAUI's <c>Connectivity.Current.NetworkAccess</c>, a browser's <c>navigator.onLine</c>) and
/// <c>Plutus.Client.Core</c> must stay free of MAUI — pinned by the architecture test
/// <c>Till_client_libraries_stay_free_of_MAUI_and_backend_modules</c>.
/// </summary>
public interface INetworkAvailability
{
    /// <summary>False ONLY when there is no usable network at all. ⚠ Must not try to be clever
    /// about whether the internet "really" works — that is the ping's job, and a platform API that
    /// guesses produces a till that says "online" in a shop whose broadband is down.</summary>
    bool HasNetwork { get; }
}

/// <summary>
/// Runs the two-step check and turns it into something an operator can act on.
///
/// ⚠ <see cref="CheckAsync"/> never throws and is bounded by <see cref="Timeout"/>. A login screen
/// that blocks on a dead network is a till that cannot be signed into during an outage — which is
/// exactly when the shop most needs to keep selling.
/// </summary>
public sealed class ConnectivityProbe
{
    /// <summary>Bounded hard: 5s is long enough for a slow shop DSL line and short enough that an
    /// operator does not think the app has hung.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Clock drift beyond this is worth telling someone about. Two minutes is the device
    /// token's own safety margin (<see cref="DeviceTokenProvider.SafetyMargin"/>) — past it, a till
    /// can believe a live token has expired, or that an expired one is still good.</summary>
    public static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromMinutes(2);

    private readonly PlutusApiClient _api;
    private readonly IDeviceCredentialStore? _credentials;
    private readonly INetworkAvailability? _network;
    private readonly Func<DateTime> _utcNow;

    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    /// <param name="api">⚠ For <c>verifyIdentity: true</c> this must be built WITH an
    /// <see cref="IDeviceTokenProvider"/>, or the device-status call goes out unauthenticated and a
    /// perfectly healthy till reads as revoked. The provider also caches the 12h token, which is
    /// what keeps a repeating probe off the token endpoint's 5/min rate limit.</param>
    /// <param name="credentials">Absent or empty means "not enrolled" — a first run, not a fault.</param>
    /// <param name="network">The OS's view. Omit and the probe always calls out.</param>
    public ConnectivityProbe(
        PlutusApiClient api,
        IDeviceCredentialStore? credentials = null,
        INetworkAvailability? network = null,
        Func<DateTime>? utcNow = null)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _credentials = credentials;
        _network = network;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// One probe. Cheap enough to run on a login screen and on a slow poll; the caller owns the
    /// cadence.
    /// </summary>
    /// <param name="verifyIdentity">False stops after the ping — use it when all the caller needs
    /// is "is the server there", e.g. while the operator is still typing a server URL during
    /// first-run setup, before any credential exists to check.</param>
    public async Task<ConnectionStatus> CheckAsync(bool verifyIdentity = true, CancellationToken ct = default)
    {
        var startedAt = _utcNow();
        var clock = Stopwatch.StartNew();

        // Step 0 — the OS. Skipping the network call when there is demonstrably no network keeps a
        // shop with a dead router off a 5-second timeout on every screen that asks.
        if (_network is { HasNetwork: false })
            return new ConnectionStatus(
                TillConnection.NoNetwork,
                "No network — this till is offline.",
                "The device reports no network connection.",
                startedAt, clock.Elapsed, null);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        // Step 1 — is a Plutus backend there at all? Anonymous, no database, no token.
        bool reached;
        PingResult? ping;
        string? detail;
        try
        {
            (reached, ping, detail) = await _api.PingAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            reached = false;
            ping = null;
            detail = $"No reply within {Timeout.TotalSeconds:0}s.";
        }

        if (!reached)
            return new ConnectionStatus(
                TillConnection.NoServer,
                "Can't reach Plutus — the network is up but the server didn't answer.",
                detail, startedAt, clock.Elapsed, null);

        // ⚠ ping may be NULL here and that is fine — an older backend with no /ping still answered,
        // which is what "reachable" means. We lose the version and the clock check, not the verdict.
        // The alternative would report every shop as offline for the whole rollout window.

        // The server's clock, against ours. Measured from the START of the call so a slow link
        // reads as latency rather than drift.
        TimeSpan? skew = ping is null ? null : ping.UtcNow - startedAt;
        var versionDetail = ping?.ApiVersion is null ? detail : $"Server v{ping.ApiVersion}";

        if (!verifyIdentity)
            return new ConnectionStatus(
                TillConnection.Online, "Server reachable.",
                versionDetail,
                startedAt, clock.Elapsed, skew);

        // Step 2 — does the platform still accept THIS till?
        //
        // ⚠ Asked via the DEVICE STATUS route, deliberately, not by minting a token. The token
        // endpoint is rate-limited to 5/min per IP: polling it would make a healthy till report
        // itself revoked (429), and in a shop where several tills share one public IP they would do
        // it to each other. Device status is one indexed lookup, gated sales.ingest, and — because
        // there is no server-side token denylist — it is also the ONLY way a revoked till finds out
        // before its 12h token expires.
        if (_credentials is null || _credentials.DeviceId is null || string.IsNullOrEmpty(_credentials.ClientSecret))
            return new ConnectionStatus(
                TillConnection.NotEnrolled,
                "Connected to Plutus — this till isn't set up yet.",
                "No device credential on this machine; run enrolment.",
                startedAt, clock.Elapsed, skew);

        try
        {
            var (code, device) = await _api.GetDeviceStatusAsync(_credentials.DeviceId.Value, timeout.Token);

            if (code == HttpStatusCode.Unauthorized || code == HttpStatusCode.Forbidden || code == HttpStatusCode.NotFound)
                return Rejected(code == HttpStatusCode.NotFound
                    ? "This till is not known to the server (device removed)."
                    : $"The server refused this till's credential (HTTP {(int)code}).");

            if (device is null)
                return new ConnectionStatus(
                    TillConnection.NoServer,
                    "Can't reach Plutus — the server answered but not with anything usable.",
                    $"Device status returned HTTP {(int)code} with no body.", startedAt, clock.Elapsed, skew);

            if (device.IsRevoked)
                return Rejected("This device has been revoked in the portal.");

            return new ConnectionStatus(
                TillConnection.Online,
                device.IsPendingRemoval
                    ? "Connected to Plutus — removal requested, still trading."
                    : "Connected to Plutus.",
                versionDetail,
                startedAt, clock.Elapsed, skew, device.Status);
        }
        catch (EnrolmentFailedException e)
        {
            // Thrown while the token provider tried to mint: the credential itself was refused.
            return Rejected(e.Message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ConnectionStatus(
                TillConnection.NoServer,
                "Can't reach Plutus — the server answered but then stopped responding.",
                $"Device status timed out after {Timeout.TotalSeconds:0}s.", startedAt, clock.Elapsed, skew);
        }
        catch (Exception e)
        {
            return new ConnectionStatus(
                TillConnection.NoServer,
                "Can't reach Plutus — the network is up but the server didn't answer.",
                $"{e.GetType().Name}: {e.Message}", startedAt, clock.Elapsed, skew);
        }

        // ⚠ Deliberately NOT "offline". The network is demonstrably fine — we just pinged it. This
        // is a portal action (restore or re-enrol the device), and saying so is the difference
        // between a manager fixing it in a minute and a shop rebooting a router all afternoon.
        ConnectionStatus Rejected(string detail) => new(
            TillConnection.Rejected,
            "Connected to Plutus, but this till has been removed or revoked.",
            detail, startedAt, clock.Elapsed, skew, "Revoked");
    }
}
