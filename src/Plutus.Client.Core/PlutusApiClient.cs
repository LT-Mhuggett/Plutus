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
