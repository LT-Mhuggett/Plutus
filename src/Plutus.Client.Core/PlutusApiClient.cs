using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Contracts.Client;

namespace Plutus.Client.Core;

/// <summary>Raised for the enrolment failures the till must present as a message, not a crash.</summary>
public sealed class EnrolmentFailedException : Exception
{
    public HttpStatusCode Status { get; }
    public EnrolmentFailedException(HttpStatusCode status, string message) : base(message) => Status = status;
}

/// <summary>
/// WP1: the till's HTTP surface. Every method is a thin, typed call onto an endpoint that already
/// exists — the retrofit is a wiring job, not a new API.
///
/// The <see cref="HttpClient"/> is INJECTED and never constructed here: that is what lets the
/// integration suite point this at <c>PlutusAppFactory</c>'s in-process client (no MySQL, no
/// network, no deployed environment) and still exercise the real controllers.
/// </summary>
public sealed class PlutusApiClient
{
    private readonly HttpClient _http;
    private readonly IDeviceTokenProvider? _tokens;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public PlutusApiClient(HttpClient http, IDeviceTokenProvider? tokens = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _tokens = tokens;
    }

    // ── reachability (anonymous) ──

    /// <summary>
    /// GET /api/v1/ping — "is there a Plutus backend at this address?", and nothing more. Sends no
    /// token, so it answers for a till that has not enrolled yet or has been revoked.
    ///
    /// ⚠ THIS NEVER THROWS. A probe that throws on a dead network makes every caller wrap it, and
    /// the one caller that forgets crashes the login screen of a till in a shop with no internet —
    /// precisely the moment the answer matters most. Failures come back as
    /// <c>Detail</c>, which is diagnostic text for the Settings panel, never for the sales floor.
    /// </summary>
    /// <summary>
    /// The outcome of a ping, as three separate facts because they have three different fixes.
    /// </summary>
    /// <param name="Reached">Did anything HTTP-shaped answer. ⚠ A **404 still means yes** — it says
    /// we are talking to a Plutus that predates this endpoint, not to a dead network. A till that
    /// read 404 as "offline" would report every shop as down for the whole rollout window (retrofit
    /// risk #7) and send people to check cables that were never the problem.</param>
    /// <param name="Supported">Does this backend actually serve a till of this generation. ⚠ FALSE
    /// on a reachable but OLDER server, and that distinction is not academic: a till reporting a
    /// confident "Connected" while its heartbeat and catalogue quietly 404 is worse than one that
    /// says the server is out of date, because the operator has no way to tell which.</param>
    public sealed record PingOutcome(bool Reached, bool Supported, PingResult? Body, string? Detail);

    /// <summary>
    /// GET /api/v1/ping — "is there a Plutus backend at this address, and is it new enough?". Sends
    /// no token, so it answers for a till that has not enrolled yet or has been revoked.
    ///
    /// ⚠ THIS NEVER THROWS. See the class note on <see cref="PingAsync"/>'s callers.
    /// </summary>
    public async Task<PingOutcome> PingAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ping");
            using var res = await _http.SendAsync(req, ct);

            if (res.StatusCode == HttpStatusCode.NotFound)
                return new PingOutcome(true, false, null,
                    "This server predates /api/v1/ping, so it also has no heartbeat or catalogue feed for this till.");

            if (!res.IsSuccessStatusCode)
                // Still an answer, so still reachable — a 500 or a 502 is a sick server, not an
                // absent one, and "check your cable" would waste the one person who could ring
                // support. Assume supported: a sick server is not an old one.
                return new PingOutcome(true, true, null, $"Server reached but answered HTTP {(int)res.StatusCode}.");

            try
            {
                return new PingOutcome(true, true, await res.Content.ReadFromJsonAsync<PingResult>(Json, ct), null);
            }
            catch (Exception e) when (e is JsonException or NotSupportedException)
            {
                // A captive portal or a proxy returning an HTML login page. Something answered, but
                // it was not Plutus.
                return new PingOutcome(false, false, null, "Something answered that address, but it was not Plutus.");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the CALLER gave up — that is not a connectivity verdict
        }
        catch (Exception e)
        {
            // Timeouts, DNS failures, TLS failures, refused connections — all of them mean the same
            // thing to a till: nothing is there.
            return new PingOutcome(false, false, null, $"{e.GetType().Name}: {e.Message}");
        }
    }

    // ── enrolment (anonymous) ──

    /// <summary>Redeem a one-time enrolment code. 410 Gone = reused/expired/unknown — surfaced as
    /// <see cref="EnrolmentFailedException"/> so the UI can say "ask for a new code" rather than
    /// dying on a null.</summary>
    public async Task<EnrolResult> EnrolAsync(string enrolmentCode, CancellationToken ct = default)
    {
        using var res = await _http.PostAsJsonAsync("/api/v1/tills/enrol", new EnrolRequest(enrolmentCode), Json, ct);
        if (!res.IsSuccessStatusCode)
            throw new EnrolmentFailedException(res.StatusCode, res.StatusCode == HttpStatusCode.Gone
                ? "That enrolment code has already been used or has expired. Ask for a new one in the portal."
                : $"Enrolment failed ({(int)res.StatusCode}).");
        return (await res.Content.ReadFromJsonAsync<EnrolResult>(Json, ct))!;
    }

    /// <summary>Exchange the device credential for a short-lived bearer token.</summary>
    public async Task<DeviceTokenResult> GetDeviceTokenAsync(Guid deviceId, string clientSecret, CancellationToken ct = default)
    {
        using var res = await _http.PostAsJsonAsync("/api/v1/tokens/device", new DeviceTokenRequest(deviceId, clientSecret), Json, ct);
        if (!res.IsSuccessStatusCode)
            throw new EnrolmentFailedException(res.StatusCode,
                res.StatusCode == HttpStatusCode.Unauthorized
                    ? "This till's credential was rejected — it may have been revoked in the portal."
                    : $"Could not get a device token ({(int)res.StatusCode}).");
        return (await res.Content.ReadFromJsonAsync<DeviceTokenResult>(Json, ct))!;
    }

    // ── authenticated reads ──

    /// <summary>
    /// Is this device still accepted? Returns the STATUS CODE as well as the body, because the
    /// codes carry the meaning: 200 answers the question, 401/403 answers it the other way, and a
    /// throw means the server never got asked.
    ///
    /// ⚠ Use THIS, not <see cref="GetDeviceTokenAsync"/>, to check a till's standing on a repeating
    /// cadence. The token endpoint is rate-limited to 5 requests/minute per IP, so polling it makes
    /// a healthy till start reporting itself as revoked (429) — and in a shop where several tills
    /// share one public IP, it makes them do it to each other.
    /// </summary>
    public async Task<(HttpStatusCode Status, DeviceStatusResult? Body)> GetDeviceStatusAsync(
        Guid deviceId, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tills/devices/{deviceId}/status");
        await AuthoriseAsync(req, ct);
        using var res = await _http.SendAsync(req, ct);
        DeviceStatusResult? body = null;
        try { if (res.IsSuccessStatusCode) body = await res.Content.ReadFromJsonAsync<DeviceStatusResult>(Json, ct); }
        catch (Exception e) when (e is JsonException or NotSupportedException) { /* non-JSON error page */ }
        return (res.StatusCode, body);
    }

    /// <summary>WP5 heartbeat. Returns null when the server did not answer usefully — the caller
    /// treats that as "no signals", never as an error worth showing a customer-facing till.</summary>
    public async Task<HeartbeatResult?> HeartbeatAsync(HeartbeatRequest body, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/heartbeat")
        {
            Content = JsonContent.Create(body, options: Json),
        };
        await AuthoriseAsync(req, ct);
        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<HeartbeatResult>(Json, ct);
    }

    /// <summary>
    /// WP5 catalogue feed. <paramref name="since"/> is the OPAQUE cursor from the previous page —
    /// store it, hand it back, never parse it. Null means "from the beginning", i.e. a full resync.
    /// </summary>
    public Task<CatalogueChangesResult?> GetCatalogueChangesAsync(
        string? since = null, int? limit = null, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (!string.IsNullOrEmpty(since)) q.Add($"since={Uri.EscapeDataString(since)}");
        if (limit is int l) q.Add($"limit={l}");
        var url = "/api/v1/catalogue/changes" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
        return GetAsync<CatalogueChangesResult>(url, ct);
    }

    public Task<TillNameResult?> GetTillNameAsync(Guid tillId, CancellationToken ct = default) =>
        GetAsync<TillNameResult>($"/api/v1/tills/{tillId}/name", ct);

    public Task<StoreInfoResult?> GetStoreInfoAsync(int storeId, CancellationToken ct = default) =>
        GetAsync<StoreInfoResult>($"/api/v1/stores/{storeId}/info", ct);

    public Task<ReceiptTemplateResult?> GetReceiptTemplateAsync(int storeId, CancellationToken ct = default) =>
        GetAsync<ReceiptTemplateResult>($"/api/v1/stores/{storeId}/receipt-template", ct);

    public Task<EffectiveThemeResult?> GetEffectiveThemeAsync(Guid? tillId, int? storeId, CancellationToken ct = default)
    {
        var q = tillId is Guid t ? $"tillId={t}" : storeId is int s ? $"storeId={s}" : "";
        return GetAsync<EffectiveThemeResult>($"/api/v1/themes/effective{(q.Length > 0 ? "?" + q : "")}", ct);
    }

    /// <summary>WP2c: the portal's published VAT bands, with their whole effective-dated timeline.
    /// Cache the result — a till applies it offline, including future-dated changes.</summary>
    public Task<VatBandsResult?> GetVatBandsAsync(CancellationToken ct = default) =>
        GetAsync<VatBandsResult>("/api/v1/vat/bands", ct);

    // ── sale ingest ──

    /// <summary>
    /// POST a sale. Returns the raw status alongside the parsed body because the STATUS is the
    /// policy: 201/200 done · 202 quarantined (never retry) · 400 failed (skip, don't block the
    /// queue) · anything else stays pending for the backoff.
    /// </summary>
    public async Task<(HttpStatusCode Status, IngestResponse? Body)> PostSaleAsync(
        string payloadJson, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sales")
        {
            Content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json"),
        };
        await AuthoriseAsync(req, ct);
        using var res = await _http.SendAsync(req, ct);
        IngestResponse? body = null;
        try { body = await res.Content.ReadFromJsonAsync<IngestResponse>(Json, ct); }
        catch (Exception e) when (e is JsonException or NotSupportedException) { /* non-JSON error page */ }
        return (res.StatusCode, body);
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        await AuthoriseAsync(req, ct);
        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return default;
        return await res.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    private async Task AuthoriseAsync(HttpRequestMessage req, CancellationToken ct)
    {
        if (_tokens == null) return;
        var token = await _tokens.GetAccessTokenAsync(ct);
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }
}
