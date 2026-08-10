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

    /// <summary>WP8: the roster of operators who may sign in at this till, with their credential
    /// hashes and raw permission windows. Cached locally so sign-in works with the network off.</summary>
    public Task<TillOperatorsResult?> GetTillOperatorsAsync(Guid tillId, CancellationToken ct = default) =>
        GetAsync<TillOperatorsResult>($"/api/v1/tills/{tillId}/operators", ct);

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

    // ── WP5b noticeboard: things a till has to put in front of a human ──

    /// <summary>Pick-from-floor notes. ⚠ Returns a BARE ARRAY, not an envelope — unlike most of this
    /// API, so there is no <c>HasMore</c> and the server caps it at 50.</summary>
    public Task<PickNoteDto[]?> GetPickNotesAsync(bool unackedOnly = true, CancellationToken ct = default) =>
        GetAsync<PickNoteDto[]>($"/api/v1/notifications?unackedOnly={(unackedOnly ? "true" : "false")}", ct);

    /// <summary>
    /// Acknowledge one pick note. Returns the raw status because the CALLER decides what a failure
    /// means, and the two cases differ: 404 is terminal (someone else acked it, or it was never
    /// this till's) while a 5xx or a dead socket is worth retrying.
    /// ⚠ Idempotent server-side — a second ack is 200, not an error.
    /// </summary>
    public async Task<HttpStatusCode> AckPickNoteAsync(Guid noteId, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/notifications/{noteId}/ack");
        await AuthoriseAsync(req, ct);
        using var res = await _http.SendAsync(req, ct);
        return res.StatusCode;
    }

    /// <summary>Active platform announcements. ⚠ Bare array, and the server has already applied
    /// both the time window and the tenant targeting — everything returned is meant for this till
    /// right now.</summary>
    public Task<AnnouncementDto[]?> GetAnnouncementsAsync(CancellationToken ct = default) =>
        GetAsync<AnnouncementDto[]>("/api/v1/announcements/active", ct);

    /// <summary>WP14: the tenant's selected card gateway, and whether a terminal integration is
    /// wired. ⚠ Read it through <c>PaymentGateway.Resolve</c> rather than acting on the fields
    /// directly — "which flow does the operator use" is a rule, not a property.</summary>
    public Task<ActiveGatewayDto?> GetActiveGatewayAsync(CancellationToken ct = default) =>
        GetAsync<ActiveGatewayDto>("/api/v1/payments/gateway/active", ct);

    /// <summary>
    /// Sign an OPERATOR in online — the same `POST /api/Auth/Login` the web till uses
    /// (binding default 11, cutover step 19).
    ///
    /// ⚠ NO DEVICE TOKEN ON THIS CALL. It is how somebody proves who they are before any session
    /// exists, so attaching the till's credential would be answering a different question. It is
    /// posted unauthenticated, exactly as the browser does it.
    ///
    /// Returns null for a wrong password, a deactivated account, a suspended tenant, or no network
    /// — the CALLER must not treat those alike, so it also hands back the status.
    /// </summary>
    public async Task<(HttpStatusCode Status, OperatorSessionDto? Session)> LoginAsync(
        string email, string password, CancellationToken ct = default)
    {
        try
        {
            using var res = await _http.PostAsJsonAsync("/api/Auth/Login", new { email, password }, Json, ct);
            if (!res.IsSuccessStatusCode) return (res.StatusCode, null);

            return (res.StatusCode, await res.Content.ReadFromJsonAsync<OperatorSessionDto>(Json, ct));
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // ⚠ Unreachable is NOT "wrong password". The caller falls back to the offline roster on
            // this, and telling an operator their password is wrong when the network is down sends
            // them to reset a credential that was never the problem.
            return (HttpStatusCode.ServiceUnavailable, null);
        }
    }

    // ── sale ingest ──

    /// <summary>
    /// Fetch a sale the platform holds (cutover step 15) — the authority for a receipt-led refund
    /// when this till never saw the original.
    ///
    /// ⚠ THE LOCAL STORE IS NOT ENOUGH, and the gap is the common case rather than the edge one:
    /// a customer returns to a DIFFERENT till from the one that sold them the goods, or comes back
    /// after the rolling window pruned the sale. Both are ordinary retail. Null means "this till
    /// cannot answer" — offline, or genuinely unknown — and a refund decided on a null is a refund
    /// decided on no evidence.
    /// </summary>
    public Task<SaleDto?> GetSaleAsync(Guid saleId, CancellationToken ct = default) =>
        GetAsync<SaleDto>($"/api/v1/sales/{saleId:D}", ct);

    /// <summary>
    /// What this till has taken, from the platform (WP11 / cutover step 26).
    ///
    /// ⚠ THE TILL CANNOT ANSWER THIS ITSELF, which is the whole reason to ask. Sales posted by the
    /// other device on the same till, sales pruned out of local history, and refunds taken at another
    /// counter against sales rung up here all belong in the figure an operator counts a drawer
    /// against. A till summing its own local sales would be confidently wrong, differently every day.
    ///
    /// ⚠ NEEDS AN OPERATOR TOKEN. Gated `perm:portal.financials.view,pos.reports.view`, and `perm:*`
    /// resolves from RBAC by the token's userId — a device token has none. Build the client with the
    /// operator provider (`PlutusApi.GetOperatorAsync` on MAUI).
    ///
    /// ⚠ PENCE. `/reports/summary-rich` returns POUNDS for the portal; confusing the two is a 100×
    /// error in a number somebody banks against.
    /// </summary>
    /// <param name="level">`till` for an X-report, `store`, or `company`.</param>
    public Task<ReportSummary?> GetReportSummaryAsync(
        string level, string id, DateOnly from, DateOnly to,
        string granularity = "day", CancellationToken ct = default) =>
        GetAsync<ReportSummary>(
            $"/api/v1/reports/summary?level={Uri.EscapeDataString(level)}&id={Uri.EscapeDataString(id)}"
            + $"&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&granularity={Uri.EscapeDataString(granularity)}", ct);

    /// <summary>
    /// Sales the PLATFORM holds for a date range — from every till unless one is named (WP11).
    ///
    /// ⚠ THIS IS HOW A CROSS-TILL REFUND BECOMES FINDABLE. A till's own list works offline and
    /// covers the common case; goods bought at another branch exist only here, and without this the
    /// operator has to type a UUID off a receipt to reach one.
    ///
    /// ⚠ NEEDS AN OPERATOR TOKEN (`perm:portal.reports.view,pos.reports.view`).
    ///
    /// ⚠ `take` is clamped 1..500 SERVER-side. Ask for what a person can actually read, not for
    /// everything — a picker of 500 sales is a picker nobody uses.
    /// </summary>
    /// <param name="tillId">Null for every till — which is the point when looking for another
    /// branch's sale.</param>
    public Task<List<SaleListEntry>?> GetSalesAsync(
        DateOnly from, DateOnly to, Guid? tillId = null, int take = 50, CancellationToken ct = default)
    {
        var url = $"/api/v1/sales?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&take={take}";
        if (tillId is Guid t) url += $"&tillId={t:D}";
        return GetAsync<List<SaleListEntry>>(url, ct);
    }

    /// <summary>
    /// Read one catalogue item from the platform, as the legacy endpoints hold it (WP10).
    ///
    /// ⚠ NEEDS AN OPERATOR TOKEN, and a device token does not merely fail the policy — it 500s.
    /// `CompositeApiControllerBaseR`'s CONSTRUCTOR does
    /// `User.Claims.First(c =&gt; c.Type == "…/objectidentifier")`, and only an operator token carries
    /// that claim. `First` on no match throws before the action ever runs.
    ///
    /// ⚠ `businessId` is the LEGACY business id and travels as a HEADER. Not the tenant id.
    /// </summary>
    public async Task<ItemDto?> GetItemAsync(string idOne, Guid businessId, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/Item/{Uri.EscapeDataString(idOne)}");
        req.Headers.Add("businessId", businessId.ToString("D"));
        await AuthoriseAsync(req, ct);

        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;

        try { return await res.Content.ReadFromJsonAsync<ItemDto>(Json, ct); }
        catch (Exception e) when (e is JsonException or NotSupportedException) { return null; }
    }

    /// <summary>
    /// The tenant's tax bands, for the item editor's Tax list (WP10 / cutover step 25).
    ///
    /// ⚠ THE SAME URL THE WEB TILL CALLS, page size and all — binding default 10, "when in doubt,
    /// match the web till". A second listing endpoint over the same table is how two tills end up
    /// offering different bands for the same item.
    ///
    /// ⚠ `Rate` is a MULTIPLIER (1.2 = 20%); see <see cref="TaxBandDto"/>.
    /// </summary>
    public Task<(List<TaxBandDto>? Bands, string? Problem)> GetTaxBandsAsync(
        Guid businessId, CancellationToken ct = default)
        => GetLegacyAsync<List<TaxBandDto>>("/api/Tax/Index?PageNumber=1&PageSize=50", businessId, "tax bands", ct);

    /// <summary>The tenant's categories, for the item editor's Category list. Same URL as the web
    /// till's `fetchCategories`.</summary>
    public Task<(List<CategoryDto>? Categories, string? Problem)> GetCategoriesAsync(
        Guid businessId, CancellationToken ct = default)
        => GetLegacyAsync<List<CategoryDto>>("/api/Category/Index?PageNumber=1&PageSize=100", businessId, "categories", ct);

    /// <summary>
    /// A GET against a LEGACY composite controller.
    ///
    /// ⚠ `businessId` travels as a HEADER on every one of these, which is why they cannot go
    /// through <c>GetAsync</c>. It is the LEGACY business id, not the tenant id — the web till
    /// sends the same header on every request (`api.ts headers()`), and the wrong one silently
    /// returns another tenant's rows or none at all.
    ///
    /// ⚠ IT RETURNS A REASON, AND THAT IS THE POINT OF THE SHAPE. This used to return `default` —
    /// null — for every failure: 400, 401, 403, 404, 500, an unparseable body, a wrapped envelope.
    /// The caller turned that into an empty list, and an empty list is indistinguishable from *"this
    /// tenant genuinely has no tax bands"*. So the item editor skipped its tax and category prompts
    /// in silence and Matt reported the feature as **missing** (2026-08-10) — which, from where he
    /// was standing, it was.
    ///
    /// ⚠ An empty list and a failed call are DIFFERENT ANSWERS and a client must not flatten them
    /// into one. "There are none" is a fact about the shop; "I could not ask" is a fact about the
    /// till, and only the second one is worth waking somebody up for.
    /// </summary>
    /// <param name="what">What was being fetched, for the message — "tax bands", "categories".</param>
    private async Task<(T? Value, string? Problem)> GetLegacyAsync<T>(
        string url, Guid businessId, string what, CancellationToken ct)
    {
        if (businessId == Guid.Empty)
            return (default, $"This till hasn't learnt which business it belongs to, so it can't load {what}.");

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("businessId", businessId.ToString("D"));
            await AuthoriseAsync(req, ct);

            using var res = await _http.SendAsync(req, ct);

            if (!res.IsSuccessStatusCode)
            {
                // ⚠ THE STATUS IS NAMED, because these three fail for genuinely different reasons
                // and the fix differs each time. ⚠ 500 in particular is the legacy base
                // controller's CONSTRUCTOR doing `.First()` on the `objectidentifier` claim — a
                // DEVICE token does not merely fail the policy, it 500s before the action runs —
                // so "signed in?" is the right question to put in front of an operator.
                var reason = (int)res.StatusCode switch
                {
                    401 => "Plutus didn't accept this till's sign-in",
                    403 => "this operator isn't allowed to read them",
                    404 => "Plutus has no such list",
                    500 => "Plutus errored — this usually means nobody is signed in on this till",
                    _ => $"Plutus answered {(int)res.StatusCode}",
                };
                return (default, $"Couldn't load {what}: {reason}.");
            }

            var value = await res.Content.ReadFromJsonAsync<T>(Json, ct);
            return (value, null);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            // ⚠ A SHAPE MISMATCH, not an empty shop. These endpoints return a BARE ARRAY today
            // (`PagedList<T>` derives from `List<T>`; the paging metadata rides in the
            // `X-Pagination` header), so this fires only if something starts wrapping the body —
            // a proxy, or a server change. Silently reporting "none" would send somebody hunting
            // through the portal's data for a fault that is in the wire.
            return (default, $"Plutus sent {what} back in a shape this till didn't recognise.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return (default, $"Couldn't reach Plutus to load {what}.");
        }
    }

    /// <summary>
    /// Change some fields of an item, safely.
    ///
    /// ⚠ READ-MODIFY-WRITE, AND THIS IS THE WHOLE POINT OF THE METHOD. `PUT /api/Item/{id1}` binds
    /// the COMPLETE entity, so anything not sent is written back as its default: a plain price
    /// change would clear `StockUntracked` (a carrier bag becomes stock-tracked) or blank
    /// `BinnedAtUtc` — **restoring a withdrawn item to sale on every till in the estate**. The web
    /// till learned this the hard way and carries the same warning in `api.ts itemBody`. Fetching
    /// first and echoing everything back is the only safe shape, so the client does not expose one
    /// that isn't.
    ///
    /// ⚠ Returns the server's own message on a 400. The band guard
    /// (`|price − exPrice × rate| ≤ 2p`) explains precisely what is wrong and what to do about it —
    /// far better than anything this layer could invent.
    /// </summary>
    /// <param name="mutate">Applied to the item as the platform currently holds it.</param>
    public async Task<(bool Ok, string? Problem)> UpdateItemFieldsAsync(
        string idOne, Guid businessId, Action<ItemDto> mutate, CancellationToken ct = default)
    {
        if (mutate is null) throw new ArgumentNullException(nameof(mutate));

        var item = await GetItemAsync(idOne, businessId, ct);
        if (item is null) return (false, "That item couldn't be read from Plutus.");

        mutate(item);

        // ⚠ The composite key must be present on the way back or the entity will not bind.
        item.Id ??= idOne;
        item.IdOne ??= idOne;
        if (item.IdTwo == Guid.Empty) item.IdTwo = businessId;
        if (item.BusinessId == Guid.Empty) item.BusinessId = businessId;

        using var req = new HttpRequestMessage(HttpMethod.Put, $"/api/Item/{Uri.EscapeDataString(idOne)}")
        {
            Content = JsonContent.Create(item, options: Json),
        };
        req.Headers.Add("businessId", businessId.ToString("D"));
        await AuthoriseAsync(req, ct);

        using var res = await _http.SendAsync(req, ct);
        if (res.IsSuccessStatusCode) return (true, null);

        var detail = await res.Content.ReadAsStringAsync(ct);
        return (false, string.IsNullOrWhiteSpace(detail) ? $"Plutus refused the change ({(int)res.StatusCode})." : detail);
    }

    /// <summary>
    /// Record a cash movement or a drawer count (WP9).
    ///
    /// ⚠ THE STATUS IS THE POLICY, exactly as it is for a sale, which is why the raw status comes
    /// back rather than a bool:
    ///   • **201** recorded · **200** an idempotent replay of an eventId already stored — both mean
    ///     "the platform has it", so the queue may drop the entry;
    ///   • **409** the business day is already Z-CLOSED. Terminal. Retrying for ever cannot help,
    ///     and the honest answer is that somebody closed the day while this was queued;
    ///   • **400** the platform refused the shape (no reason on a paid-out, no count on a Z).
    ///     Terminal — a till cannot fix it by asking again;
    ///   • anything else is transport, and stays queued for the backoff.
    ///
    /// ⚠ A DEVICE token is enough (`sales.ingest`), on purpose. Declaring a float and closing a day
    /// are the till's own record of its own drawer, and they must work when the shop opens before
    /// anyone has signed in — the same reasoning that gates the heartbeat this way.
    /// </summary>
    public async Task<(HttpStatusCode Status, CashEventResult? Body)> PostCashEventAsync(
        CashEventRequest body, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cash-events")
        {
            Content = JsonContent.Create(body, options: Json),
        };
        await AuthoriseAsync(req, ct);
        using var res = await _http.SendAsync(req, ct);

        CashEventResult? parsed = null;
        try
        {
            if (res.IsSuccessStatusCode)
                parsed = await res.Content.ReadFromJsonAsync<CashEventResult>(Json, ct);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            // ⚠ A non-JSON error page must not look like a failure to POST. The STATUS already told
            // the caller what to do; the body is a bonus.
        }

        return (res.StatusCode, parsed);
    }

    /// <summary>
    /// The day's cash events for one till — the X/Z history, and what the drawer was expected to
    /// hold at each count.
    ///
    /// ⚠ NEEDS AN OPERATOR TOKEN. This is gated `perm:portal.financials.view,pos.reports.view`, and
    /// `perm:*` policies resolve from RBAC by the token's **userId** — a device token has none, so a
    /// device-authorised client gets 403 for ever. Build this client with the OPERATOR provider
    /// (`PlutusApi.GetOperatorAsync` on MAUI). Reading the day's takings is a question about a
    /// PERSON's permissions; recording the float is not, which is why only one of the two needs it.
    /// </summary>
    public Task<List<CashEventResult>?> GetCashEventsAsync(
        Guid tillId, DateOnly day, CancellationToken ct = default) =>
        GetAsync<List<CashEventResult>>(
            $"/api/v1/cash-events?tillId={tillId:D}&day={day:yyyy-MM-dd}", ct);

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
