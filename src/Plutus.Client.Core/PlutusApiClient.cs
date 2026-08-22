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

    /// <summary>
    /// ⚠⚠ `ApiTime.Utc` IS WHY EVERY `DateTime` OFF THIS CLIENT MEANS WHAT IT SAYS (2026-08-21).
    ///
    /// Without it, a timestamp that came off a MySQL column arrives as `"2026-08-21T14:30:00"` — no
    /// `Z` — and `System.Text.Json` hands back `DateTimeKind.Unspecified`. **`.ToLocalTime()` on an
    /// `Unspecified` value does nothing at all**, so the Sales report read an hour early all summer
    /// and would have read correctly all winter. Matt found it on the web till, which had the
    /// identical fault in TypeScript: *"Why are the sales a correct time on the portal and an hour
    /// earlier on the webtill?"*
    ///
    /// ⚠ HERE, NOT AT THE CALL SITES. Thirteen `.ToLocalTime()`s were wrong; fixing thirteen call
    /// sites leaves the fourteenth to be written wrong. See `ApiTime`, and `till-design.md` C2 for
    /// what pins this to the two TypeScript copies.
    /// </summary>
    public static readonly JsonSerializerOptions Json = Configure(new(JsonSerializerDefaults.Web));

    /// <summary>⚠ Both converters, always — `JsonConverter&lt;DateTime&gt;` does not cover `DateTime?`.</summary>
    private static JsonSerializerOptions Configure(JsonSerializerOptions o)
    {
        o.Converters.Add(new Plutus.SharedKernel.ApiTime.Utc());
        o.Converters.Add(new Plutus.SharedKernel.ApiTime.UtcNullable());
        return o;
    }

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

    /// <summary>
    /// FE3.0 — report what this till found when it polled its local hardware agent, so the portal's
    /// Locations page can see the fleet's printers.
    ///
    /// ⚠ Send a null <paramref name="agentVersion"/> when there is no agent: "this till PC has none"
    /// is a fleet fact the portal displays, and skipping it makes a till with no agent look identical
    /// to a till that has never reported.
    ///
    /// ⚠ ASK <see cref="AgentReporting.ShouldSend"/> FIRST. It is telemetry the server overwrites in
    /// place, and MAUI's cadence is 60s against the web till's 5 minutes — beating on it every tick
    /// would be hundreds of times the useful traffic to rewrite a row with what it already holds.
    /// </summary>
    public Task<bool> ReportAgentStatusAsync(
        Guid deviceId, string? agentVersion, string? printerName, bool? printerOnline,
        CancellationToken ct = default)
        => PostJsonAsync(
            "/api/v1/tills/agent-status",
            new AgentStatusRequest(deviceId, agentVersion, printerName, printerOnline), ct);

    /// <summary>
    /// Ask for this till to be taken off the estate (WP4, step 21).
    ///
    /// ⚠ THE TILL'S OWN DEVICE ID, always. The server refuses any other with a 403 — a till may only
    /// un-enrol itself, or one enrolled till could start the removal of every other till in the
    /// estate and the portal's approval queue would fill with plausible requests.
    ///
    /// ⚠ NOTHING IS REMOVED BY THIS. The device becomes <c>PendingRemoval</c> and keeps trading
    /// until a human approves it in the portal — so the caller must not tell an operator the till
    /// has been un-enrolled.
    /// </summary>
    public Task<bool> RequestUnenrolAsync(Guid deviceId, CancellationToken ct = default) =>
        PostJsonAsync("/api/v1/tills/unenrol-request", new UnenrolRequest(deviceId), ct);

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

    /// <summary>
    /// The shop's scheduled discount rules — "Wednesday Warhammer".
    ///
    /// ⚠ Cache the result: the schedule arrives RAW and the TILL decides whether it is Wednesday, so
    /// a cached rule set keeps discounting correctly for as long as the till keeps trading. That is
    /// the whole reason the server does not pre-evaluate it — see <see cref="DiscountRuleDto"/>.
    /// </summary>
    public Task<DiscountRulesResult?> GetDiscountRulesAsync(CancellationToken ct = default) =>
        GetAsync<DiscountRulesResult>("/api/v1/discounts/rules", ct);

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

    // ── OP4 / WP6.3 support tickets: the till's "ask for help", and the reply ──

    /// <summary>This tenant's tickets, newest activity first. ⚠ Bare array; gated on
    /// <c>support.tickets</c>, which is seeded to every role.</summary>
    public Task<SupportTicketDto[]?> GetSupportTicketsAsync(CancellationToken ct = default) =>
        GetAsync<SupportTicketDto[]>("/api/v1/support/tickets", ct);

    /// <summary>One ticket's thread, oldest first.</summary>
    public Task<SupportMessageDto[]?> GetSupportThreadAsync(Guid ticketId, CancellationToken ct = default) =>
        GetAsync<SupportMessageDto[]>($"/api/v1/support/tickets/{ticketId}/messages", ct);

    /// <summary>
    /// Raise a ticket. True when the platform has it.
    ///
    /// ⚠⚠ NOT QUEUED OFFLINE, and that is deliberate. A ticket is somebody asking for help NOW;
    /// silently parking it in the outbox would tell them it had been sent, and the one thing worse
    /// than a shop that cannot reach support is a shop that believes it already has.
    /// </summary>
    public Task<bool> RaiseSupportTicketAsync(
        string subject, string body, byte severity, CancellationToken ct = default) =>
        PostJsonAsync("/api/v1/support/tickets", new RaiseTicketRequest(subject, body, severity), ct);

    /// <summary>Reply on a thread. ⚠ Same reasoning as raising one — never queued.</summary>
    public Task<bool> ReplyToSupportTicketAsync(Guid ticketId, string body, CancellationToken ct = default) =>
        PostJsonAsync($"/api/v1/support/tickets/{ticketId}/messages", new TicketReplyRequest(body), ct);

    // ── WP-TICKETS, 2026-08-21 ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// How many replies this shop has not read.
    ///
    /// ⚠ THE TILL ALSO GETS THIS ON THE HEARTBEAT (`HeartbeatResult.UnreadSupportReplies`), which is
    /// the channel that reaches a till with nobody watching a screen. This one exists for the moment
    /// Help is opened, when a fresh number matters more than a cheap one.
    /// </summary>
    public Task<UnreadSupportDto?> GetUnreadSupportAsync(CancellationToken ct = default) =>
        GetAsync<UnreadSupportDto>("/api/v1/support/unread", ct);

    /// <summary>⚠ Called when a THREAD IS OPENED — the badge is a consequence of the state, never
    /// the owner of it, or the other till in the shop stays lit.</summary>
    public Task<bool> MarkTicketReadAsync(Guid ticketId, CancellationToken ct = default) =>
        PostJsonAsync($"/api/v1/support/tickets/{ticketId}/read", new { }, ct);

    /// <summary>⚠ A REQUEST, NOT A CLOSE: a shop closing its own open incident is how a fault gets
    /// lost. Plutus support confirms.</summary>
    public Task<bool> RequestTicketCloseAsync(Guid ticketId, CancellationToken ct = default) =>
        PostJsonAsync($"/api/v1/support/tickets/{ticketId}/request-close", new { }, ct);

    /// <summary>Withdraw your own request, or decline support's — either party may end the question.</summary>
    public Task<bool> KeepTicketOpenAsync(Guid ticketId, CancellationToken ct = default) =>
        PostJsonAsync($"/api/v1/support/tickets/{ticketId}/keep-open", new { }, ct);

    /// <summary>⚠ Only valid while SUPPORT has asked; without a standing request the server 400s and
    /// this answers false.</summary>
    public Task<bool> AcceptTicketCloseAsync(Guid ticketId, CancellationToken ct = default) =>
        PostJsonAsync($"/api/v1/support/tickets/{ticketId}/accept-close", new { }, ct);

    /// <summary>WP14: the tenant's selected card gateway, and whether a terminal integration is
    /// wired. ⚠ Read it through <c>PaymentGateway.Resolve</c> rather than acting on the fields
    /// directly — "which flow does the operator use" is a rule, not a property.</summary>
    public Task<ActiveGatewayDto?> GetActiveGatewayAsync(CancellationToken ct = default) =>
        GetAsync<ActiveGatewayDto>("/api/v1/payments/gateway/active", ct);

    /// <summary>
    /// Which reports the portal has published to this till — ruling 5b(a).
    ///
    /// ⚠⚠ NULL MEANS "COULD NOT ASK", NOT "NOTHING PUBLISHED". Offline, or on any failure, the caller
    /// must fall back to its last known list and then to the FULL catalogue — a till that loses its
    /// Reports tab because the network blinked is worse than one showing a report an owner meant to hide.
    /// The server never answers "not configured" with an error for the same reason.
    ///
    /// ⚠ The keys come back already filtered to what this build knows and in catalogue order, so a menu
    /// can render them directly. ⚠ Still run each through <c>ReportPermissions.MayRead</c>: the publish
    /// decides the menu, the permission decides the door.
    /// </summary>
    public Task<PublishedReportsDto?> GetPublishedReportsAsync(Guid? tillId, CancellationToken ct = default) =>
        GetAsync<PublishedReportsDto>(
            tillId.HasValue
                ? $"/api/v1/reports/published?tillId={tillId.Value}"
                : "/api/v1/reports/published",
            ct);

    /// <summary>
    /// The carrier bags the portal set for this shop, cheapest first — ruling 2026-08-19.
    ///
    /// ⚠⚠ NULL MEANS "COULD NOT ASK", NOT "NO BAGS". An EMPTY list is a real answer — a shop that
    /// stopped selling bags — and must replace the caller's cache; null must not.
    ///
    /// ⚠⚠ THE FALLBACK DIRECTION IS THE OPPOSITE OF THE PUBLISHED-REPORTS CALL ABOVE, and deliberately.
    /// A missing Reports tab is worse than an extra report, so that one fails towards MORE. A bag is
    /// MONEY: failing towards a guessed price charges a customer something the shop never set, so this
    /// one fails towards NO BUTTON — cached last-known-good, then nothing.
    /// </summary>
    public Task<IReadOnlyList<CarrierBagDto>?> GetCarrierBagsAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<CarrierBagDto>>("/api/v1/carrier-bags", ct);

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

    // ── WP8 employees: the till's Users screen (step 24) ──

    /// <summary>
    /// The business's employees, same URL and page size as the web till's `fetchEmployees`.
    ///
    /// ⚠⚠ THE 100-ROW CAP IS THE WEB TILL'S, and it is a real cap, not a default. A business with
    /// more than 100 employees silently sees the first 100 — ordered by `CreatedAt`, so it is the
    /// NEWEST staff who disappear, which is exactly backwards for a screen used to set a new
    /// starter's password. Kept identical on purpose (binding default 10); the caller surfaces it.
    /// </summary>
    public Task<(List<EmployeeDto>? Employees, string? Problem)> GetEmployeesAsync(
        Guid businessId, CancellationToken ct = default)
        => GetLegacyAsync<List<EmployeeDto>>(
            "/api/Employee/Index?PageNumber=1&PageSize=100", businessId, "the staff list", ct);

    /// <summary>
    /// Create an employee. ⚠ The caller mints the id — see <see cref="CreateEmployeeRequest"/>.
    /// </summary>
    public async Task<(bool Ok, string? Problem)> CreateEmployeeAsync(
        CreateEmployeeRequest employee, Guid businessId, CancellationToken ct = default)
    {
        if (employee is null) throw new ArgumentNullException(nameof(employee));

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/Employee")
        {
            Content = JsonContent.Create(employee, options: Json),
        };
        req.Headers.Add("businessId", businessId.ToString("D"));
        await AuthoriseAsync(req, ct);

        using var res = await _http.SendAsync(req, ct);
        if (res.IsSuccessStatusCode) return (true, null);

        var detail = await res.Content.ReadAsStringAsync(ct);
        return (false, string.IsNullOrWhiteSpace(detail)
            ? $"Plutus refused the new person ({(int)res.StatusCode})."
            : detail);
    }

    /// <summary>
    /// Set or reset an employee's password.
    ///
    /// ⚠ IT TAKES EFFECT ON THE NEXT SIGN-IN, not immediately — an operator already signed in on
    /// another till stays signed in, because their session is a bearer token with no denylist. That
    /// is the same fact the roster cadence exists to work around, and the caller must not promise
    /// otherwise.
    /// </summary>
    public async Task<(bool Ok, string? Problem)> SetEmployeePasswordAsync(
        Guid employeeId, string email, string password, Guid businessId, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/Auth/SetPassword")
        {
            Content = JsonContent.Create(new SetPasswordRequest(employeeId, email, password), options: Json),
        };
        req.Headers.Add("businessId", businessId.ToString("D"));
        await AuthoriseAsync(req, ct);

        using var res = await _http.SendAsync(req, ct);
        if (res.IsSuccessStatusCode) return (true, null);

        var detail = await res.Content.ReadAsStringAsync(ct);
        return (false, string.IsNullOrWhiteSpace(detail)
            ? $"Plutus refused the new password ({(int)res.StatusCode})."
            : detail);
    }

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

    // ── categories: the MANAGEMENT endpoints (WP10 / cutover step 25) ────────────────────────
    //
    // ⚠ NOT the legacy `/api/Category` CRUD, and never its DELETE. `Item → Category` is configured
    // `OnDelete(Cascade)`, so deleting a category on the legacy route silently deletes every item in
    // it — and their sale lines and stock with them. The v1 controller exists to guard exactly that:
    // it refuses (409) while any item still references the category, and refuses the LAST category
    // because `Item.CatId` is required. Reassign first, then delete.

    /// <summary>Categories WITH their item counts — the number the reassign-first flow turns on.
    /// ⚠ Needs `portal.reports.view`.</summary>
    public Task<List<CategoryListDto>?> GetCategoryListAsync(CancellationToken ct = default)
        => GetAsync<List<CategoryListDto>>("/api/v1/categories", ct);

    /// <summary>Create a category. ⚠ 409 when the name is taken — names are unique per tenant.</summary>
    public Task<(bool Ok, string? Problem)> CreateCategoryAsync(
        string name, string? description = null, CancellationToken ct = default)
        => WriteCategoryAsync(HttpMethod.Post, "/api/v1/categories",
            new { name, description = description ?? "" }, "create that category", ct);

    /// <summary>Rename a category. ⚠ A blank description leaves the existing one alone — unlike
    /// create, where blank defaults to the name.</summary>
    public Task<(bool Ok, string? Problem)> RenameCategoryAsync(
        Guid id, string name, CancellationToken ct = default)
        => WriteCategoryAsync(HttpMethod.Put, $"/api/v1/categories/{id:D}",
            new { name, description = "" }, "rename that category", ct);

    /// <summary>
    /// Move EVERY item out of one category into another.
    ///
    /// ⚠ IT IS UNCONDITIONAL AND HAS NO CAP. There is no partial or selective reassign — this moves
    /// the whole category, which is what makes a delete possible and also what makes it worth
    /// confirming with a COUNT in front of the operator first.
    /// </summary>
    public Task<(bool Ok, string? Problem)> ReassignCategoryAsync(
        Guid fromId, Guid toId, CancellationToken ct = default)
        => WriteCategoryAsync(HttpMethod.Post, $"/api/v1/categories/{fromId:D}/reassign",
            new { toId }, "move those items", ct);

    /// <summary>
    /// Delete a category.
    ///
    /// ⚠ **409 IS THE NORMAL ANSWER, NOT AN ERROR** — it means items are still in it, and the
    /// server's message says how many. That refusal is the reassign-first flow: a client that
    /// treats it as a failure and stops has removed the only route through.
    /// </summary>
    public Task<(bool Ok, string? Problem)> DeleteCategoryAsync(Guid id, CancellationToken ct = default)
        => WriteCategoryAsync(HttpMethod.Delete, $"/api/v1/categories/{id:D}", null, "delete that category", ct);

    private async Task<(bool Ok, string? Problem)> WriteCategoryAsync(
        HttpMethod method, string url, object? body, string what, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(method, url);
            if (body is not null) req.Content = JsonContent.Create(body, options: Json);
            await AuthoriseAsync(req, ct);

            using var res = await _http.SendAsync(req, ct);
            if (res.IsSuccessStatusCode) return (true, null);

            // ⚠ THE SERVER'S OWN WORDS, and they are the useful part. Its 409s name the count
            // ("12 item(s) are still in this category — reassign them first") and its 400s name the
            // rule. Replacing them with "couldn't do that" throws away the only actionable thing.
            var detail = await res.Content.ReadAsStringAsync(ct);
            return (false, string.IsNullOrWhiteSpace(detail)
                ? $"Plutus wouldn't {what} ({(int)res.StatusCode})."
                : detail);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return (false, $"Couldn't reach Plutus to {what}.");
        }
    }

    /// <summary>
    /// Post a stock movement — a signed DELTA against an item (WP10 / cutover step 25).
    ///
    /// ⚠ IT IS A DELTA, NOT A COUNT, AND THIS IS THE EASIEST THING IN THE WHOLE STEP TO GET WRONG.
    /// Stock is an append-only ledger: `qty` is what CHANGES, and `StockLevel` is a materialised sum
    /// the server maintains with `level.Quantity += qtyDelta`. Sending the number an operator typed
    /// into a box labelled "quantity" would ADD their count to the existing count — 7 on the shelf,
    /// operator counts 7, stock becomes 14, and nothing errors.
    ///
    /// The only "set it to N" surface in the v1 API is `POST /api/v1/stock/takes`, which converts
    /// counted − expected server-side. This client deliberately does NOT expose it: that endpoint
    /// sits under a controller-wide portal gate that also covers inter-store transfers, and widening
    /// it to reach a till would grant transfers by accident. A till adjusts; a stock take is a
    /// portal job until it has its own gate.
    ///
    /// ⚠ `Reason` IS REQUIRED for anything but a receipt, and the server refuses without it — which
    /// is right: an unexplained stock correction is indistinguishable from shrinkage being hidden.
    /// ⚠ A WriteOff must be NEGATIVE; the server refuses a positive one.
    /// ⚠ Zero is refused. A movement that moves nothing is a ledger row that means nothing.
    ///
    /// ⚠ Needs `portal.stock.adjust` OR `pos.stock.adjust` — Owner / Company Admin / Store Manager
    /// / Supervisor. Never a cashier.
    /// </summary>
    /// <param name="type">`Adjustment` (± with a reason) or `WriteOff` (negative only).</param>
    /// <param name="qtyDelta">⚠ The CHANGE, signed. Never the counted total.</param>
    public async Task<(bool Ok, string? Problem)> PostStockMovementAsync(
        string itemIdOne, string type, int qtyDelta, string reason, int? storeId = null,
        CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["itemIdOne"] = itemIdOne,
            ["type"] = type,
            ["qty"] = qtyDelta,
            ["reason"] = reason,
            ["storeId"] = storeId,
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/stock/movements")
            {
                Content = JsonContent.Create(body, options: Json),
            };
            await AuthoriseAsync(req, ct);

            using var res = await _http.SendAsync(req, ct);
            if (res.IsSuccessStatusCode) return (true, null);

            // ⚠ The server's own words. Its 400s name the rule that was broken ("a write-off must
            // have a negative qty", "reason is required") and those are the actionable sentences.
            var detail = await res.Content.ReadAsStringAsync(ct);
            return (false, string.IsNullOrWhiteSpace(detail)
                ? $"Plutus wouldn't record that stock change ({(int)res.StatusCode})."
                : detail);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // ⚠ NOT QUEUED, and that is deliberate. A sale is queued because the money moved
            // whether or not the platform heard about it; a stock correction is a DECISION, and
            // replaying one made against a count that has since changed writes the wrong number.
            return (false, "Couldn't reach Plutus. The stock change has NOT been recorded — try again when the till is back online.");
        }
    }


    /// <summary>
    /// The items in the Bin — WP10 #4, 2026-08-22.
    ///
    /// ⚠⚠ **THIS EXISTS BECAUSE THERE IS NOTHING LOCAL TO RESTORE FROM.** A binned item reaches the
    /// till as a **tombstone**: `CatalogueChangesController` sends `Removed: r.BinnedAtUtc != null`,
    /// and the till DELETES it from its local catalogue — deliberately, so a binned item stops
    /// scanning even on a till that has been offline since. MAUI's item list is a capped read of
    /// that local SQLite, so the Bin is not merely hidden there, it is **absent**.
    ///
    /// ⚠ SO THIS IS ONLINE-ONLY, and the screen says so rather than rendering an empty list. An
    /// empty Bin and an unreachable server look identical otherwise, and one of them means "nothing
    /// was withdrawn" while the other means "ask again later".
    ///
    /// ⚠ THE LEGACY `Index` ENDPOINT, not a v1 route: it is what the portal's Bin view uses
    /// (`fetchCatalogueItemsPaged(..., binned = true)`), and a second server-side definition of
    /// "which items are in the Bin" is a definition that can disagree with the first.
    /// </summary>
    /// <returns>Null when it could not be read — never an empty list, which would read as "the Bin
    /// is empty".</returns>
    /// <remarks>
    /// ⚠⚠ `businessId` TRAVELS AS A HEADER AND IS NOT OPTIONAL — added 2026-08-22 after the Bin
    /// shipped without it and could not read anything at all. The legacy endpoint is
    /// `CompositeApiControllerBaseR.Index([FromHeader] TId2 businessId, …)`: with no header it answers
    /// a plain-text **400 "Business ID not provided"**, which this method saw only as
    /// `!IsSuccessStatusCode` and reported to the operator as *"the bin couldn't be read... try again
    /// when the till is back online"* — a connectivity message for a request that never had a hope.
    ///
    /// ⚠ THE LESSON WAS ALREADY WRITTEN DOWN AND I STILL MISSED IT. `BinRestoreE2eTests` carries the
    /// trap in its own comment, because the integration test hit exactly this and had to add the
    /// header to pass. The test knew; the client did not. Every sibling here — `GetItemAsync`,
    /// `CreateItemAsync`, `UpdateItemFieldsAsync` — takes a `businessId` and sends the header.
    ///
    /// ⚠ It is the LEGACY business id, not the tenant id.
    /// </remarks>
    public async Task<IReadOnlyList<BinnedItemDto>?> GetBinnedItemsAsync(
        Guid businessId, string? search = null, int page = 1, int pageSize = 100, CancellationToken ct = default)
    {
        var url = $"/api/Item/Index?PageNumber={page}&PageSize={pageSize}&Binned=true"
                + (string.IsNullOrWhiteSpace(search) ? "" : $"&Search={Uri.EscapeDataString(search.Trim())}");

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("businessId", businessId.ToString("D"));
            await AuthoriseAsync(req, ct);
            using var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return null;

            return await res.Content.ReadFromJsonAsync<BinnedItemDto[]>(Json, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // ⚠ Null, not empty. See the summary — the two mean opposite things to an operator.
            return null;
        }
    }
    /// <summary>
    /// Move items to the Bin, or bring them back (WP10 / cutover step 25).
    ///
    /// ⚠ THE BIN IS A SOFT DELETE AND THAT IS THE WHOLE DESIGN. Binning stamps `BinnedAtUtc`; the
    /// catalogue feed then sends the item as a TOMBSTONE (`Removed: true`) rather than omitting it,
    /// because "not in this page" and "withdrawn from sale" are indistinguishable to a client that
    /// only ever receives upserts. Every read path on the till honours it, so a binned item stops
    /// scanning even on a till that has been offline since — which is the point when the withdrawal
    /// is a recall.
    ///
    /// ⚠ NOTHING IS DESTROYED. The item keeps its id, so restoring it does not split its sales
    /// history in two, and past sale lines still resolve.
    ///
    /// ⚠ Needs `inventory.bulk` — Owner, Company Admin and Store Manager. Withdrawing a product
    /// from sale across the whole estate is deliberately not a cashier's action.
    /// </summary>
    /// <param name="bin">True to bin, false to restore.</param>
    public async Task<(bool Ok, string? Problem)> BinItemsAsync(
        IReadOnlyList<string> itemIdOnes, bool bin, CancellationToken ct = default)
    {
        if (itemIdOnes is null || itemIdOnes.Count == 0) return (true, null);

        var body = new Dictionary<string, object?>
        {
            ["action"] = bin ? "bin" : "restore",
            ["ids"] = itemIdOnes,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/items/bulk")
        {
            Content = JsonContent.Create(body, options: Json),
        };
        await AuthoriseAsync(req, ct);

        using var res = await _http.SendAsync(req, ct);
        if (res.IsSuccessStatusCode) return (true, null);

        // ⚠ The SERVER's words on a 409 — it refuses a bulk over its per-call limit and says what
        // the limit is, which is more useful than anything this layer could invent.
        var detail = await res.Content.ReadAsStringAsync(ct);
        return (false, string.IsNullOrWhiteSpace(detail)
            ? $"Plutus refused that ({(int)res.StatusCode})."
            : detail);
    }

    /// <summary>
    /// On-hand quantity for a PAGE of items (WP10 / cutover step 25).
    ///
    /// ⚠ ONE CALL PER VISIBLE PAGE, NEVER ONE PER ROW — the endpoint is a bulk POST for exactly
    /// that reason, and it clamps to 200 ids server-side. A per-row call over a 500-row list is 500
    /// round trips on a counter with a queue.
    ///
    /// ⚠ IT IS NOT IN THE CATALOGUE FEED, and deliberately so. Stock moves on every sale on every
    /// till in the shop; pushing it down the effective-dated catalogue feed would either flood the
    /// feed or ship a number already stale by the time it arrived. A quantity is read when a screen
    /// asks, or not at all.
    ///
    /// ⚠ Needs an OPERATOR token — `portal.reports.view` OR `pos.reports.view`. Returns null on any
    /// failure, because an inventory list must still render without counts; the column shows "—",
    /// which is what it showed before this call existed.
    /// </summary>
    public async Task<List<StockLevelDto>?> GetStockLevelsAsync(
        IReadOnlyList<string> itemIdOnes, CancellationToken ct = default)
    {
        if (itemIdOnes is null || itemIdOnes.Count == 0) return new List<StockLevelDto>();

        // ⚠ Clamped HERE as well as server-side. The server silently TAKES the first 200 rather than
        // refusing, so sending 500 would come back with 200 answers and 300 silent gaps — every one
        // of which would render as "never counted".
        var page = itemIdOnes.Count > 200 ? itemIdOnes.Take(200).ToArray() : itemIdOnes.ToArray();

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/stock/levels/bulk")
        {
            Content = JsonContent.Create(page, options: Json),
        };
        await AuthoriseAsync(req, ct);

        try
        {
            using var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return null;
            return await res.Content.ReadFromJsonAsync<List<StockLevelDto>>(Json, ct);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException
                                    or HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Create a catalogue item (WP10 / cutover step 25, the add-unknown-scan flow).
    ///
    /// ⚠ CHECK THE BARCODE IS FREE FIRST — with <see cref="GetItemAsync"/> — and this method does
    /// NOT do it for you, because the honest check needs a screen: the clashing item has to be
    /// shown to the operator so they can decide whether they have just scanned something already in
    /// the catalogue. The web till learned this the hard way (`InventoryPage.tsx checkBarcodeFree`):
    /// without it the composite primary key rejects the insert and the till showed a raw "API 500".
    ///
    /// ⚠ THE SAME `POST /api/Item` THE WEB TILL USES, with the same body — binding default 10.
    /// A parallel v2 create would be a second door onto one table, and the two would drift on
    /// exactly the fields nobody checks: `brand` defaulting to "-" rather than "", `amount` 0,
    /// `image` null.
    ///
    /// ⚠ IT DOES NOT CREATE STOCK. Stock is a separate call (<see cref="CreateStockAsync"/>) and a
    /// separate decision — an item can exist with none, and a service or carrier bag should never
    /// have a count at all.
    /// </summary>
    public async Task<(bool Ok, string? Problem)> CreateItemAsync(
        ItemDto item, Guid businessId, CancellationToken ct = default)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));

        // ⚠ The composite key must be present or the entity will not bind — the same trap the PUT
        // has, and the reason `UpdateItemFieldsAsync` sets these too.
        item.Id ??= item.IdOne;
        item.IdOne ??= item.Id;
        if (item.IdTwo == Guid.Empty) item.IdTwo = businessId;
        if (item.BusinessId == Guid.Empty) item.BusinessId = businessId;

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/Item")
        {
            Content = JsonContent.Create(item, options: Json),
        };
        req.Headers.Add("businessId", businessId.ToString("D"));
        await AuthoriseAsync(req, ct);

        using var res = await _http.SendAsync(req, ct);
        if (res.IsSuccessStatusCode) return (true, null);

        var detail = await res.Content.ReadAsStringAsync(ct);
        return (false, string.IsNullOrWhiteSpace(detail)
            ? $"Plutus refused the new item ({(int)res.StatusCode})."
            : detail);
    }

    /// <summary>
    /// Give a new item its opening stock.
    ///
    /// ⚠ `bussinessId` IS SPELT THAT WAY ON THE WIRE. It is the legacy `StockBody` property name
    /// and the web till sends the same misspelling (`api.ts createStock`) — correcting it here
    /// would bind to nothing and silently create stock of zero. ⚠ The typo is load-bearing.
    /// </summary>
    public async Task<(bool Ok, string? Problem)> CreateStockAsync(
        string itemIdOne, int quantity, Guid businessId, int storeId, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["id"] = itemIdOne,
            ["itemIdOne"] = itemIdOne,
            ["quantity"] = quantity,
            ["bussinessId"] = businessId,   // ⚠ sic — see above
            ["storeId"] = storeId,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/Stock")
        {
            Content = JsonContent.Create(body, options: Json),
        };
        req.Headers.Add("businessId", businessId.ToString("D"));
        await AuthoriseAsync(req, ct);

        using var res = await _http.SendAsync(req, ct);
        if (res.IsSuccessStatusCode) return (true, null);

        var detail = await res.Content.ReadAsStringAsync(ct);
        return (false, string.IsNullOrWhiteSpace(detail)
            ? $"The item was created but its opening stock was refused ({(int)res.StatusCode})."
            : detail);
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

    // ── customers, membership, store credit (WP12 / step 27) ─────────────────────────────────────
    //
    // ⚠⚠ THERE WAS NOTHING HERE AT ALL until 2026-08-13, which is why the MAUI till has no customer
    // attach and why a Gold member is charged 10% more on it than on the web till for the same
    // basket. `Plutus.Frontend.WebApp/src/api.ts:309–418` is the reference (binding default 10 —
    // when in doubt, match the web till), and these mirror it method for method.
    //
    // ⚠ READS ARE OPEN TO ANY AUTHENTICATED PRINCIPAL, WRITES NEED AN OPERATOR TOKEN, and the split
    // matters on a till: `CustomersController`'s reads (search, detail, tier catalogue) are
    // deliberately ungated so a till can look a member up with its DEVICE token, while create and
    // set-tier are `perm:*` gated — and `perm:*` policies resolve from RBAC **by the token's
    // userId**, which a device token does not have (runbook pitfall 5). So a write attempted on a
    // device token fails as a permission error rather than a login prompt, and the message has to
    // say so.
    //
    // ⚠ ALL OF IT IS ONLINE-ONLY, and not because nobody has written the offline path: member
    // numbers come from a tenant-wide counter, so two disconnected tills would mint the same one
    // (binding defaults 20/21). The offline story is a bounded read-through cache serving name/tier
    // as a HINT — never an input to redemption maths — and that lives in the till, not here.

    /// <summary>A customer as the search list shows them. ⚠ Not sealed — <see cref="CustomerDetailDto"/>
    /// extends it, mirroring the web till's `CustomerDetail extends CustomerSummary`.</summary>
    public class CustomerSummaryDto
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        /// <summary>FE2 membership number — printed on the card as a "C…" Code 39 barcode. Validate
        /// and route scans of it with <see cref="Plutus.SharedKernel.MemberNumbers"/>.</summary>
        public string? MemberNo { get; set; }
    }

    /// <summary>Membership as the detail read returns it. ⚠ <see cref="Expired"/> is the server's
    /// verdict, not something a till re-derives from <see cref="RenewalDay"/> — one clock decides.</summary>
    public sealed class MembershipDto
    {
        public Guid? TierId { get; set; }
        public string? Tier { get; set; }
        public decimal AutoDiscountRate { get; set; }
        public string? RenewalDay { get; set; }
        public bool Expired { get; set; }
    }

    /// <summary>
    /// The full customer read — what the sale screen needs to attach somebody.
    ///
    /// ⚠ <see cref="CreditBalancePence"/> IS A LIVE FIGURE AND MUST BE TREATED AS ONE. It is the sum
    /// of an append-only ledger that another till, or the webstore, may have spent from a second
    /// ago. It is fetched per attach for that reason, and a cached copy is a hint only.
    /// </summary>
    public sealed class CustomerDetailDto : CustomerSummaryDto
    {
        /// <summary>"C" + <see cref="CustomerSummaryDto.MemberNo"/> — what a scanner reads.</summary>
        public string? MemberBarcode { get; set; }
        public Guid? CreditAccountId { get; set; }
        public long CreditBalancePence { get; set; }
        public MembershipDto? Membership { get; set; }
    }

    /// <summary>A tenant's loyalty tier. ⚠ DEFINED IN THE PORTAL ONLY (binding default 20) — a till
    /// assigns one, never creates one.</summary>
    public sealed class LoyaltyTierDto
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public decimal AutoDiscountRate { get; set; }
        public int DurationMonths { get; set; }
        public bool Active { get; set; }
        public int SortOrder { get; set; }
        public int MemberCount { get; set; }
    }

    /// <summary>
    /// Search customers for the attach box — name, email, phone, or a membership number.
    ///
    /// ⚠ A SCANNED CARD RESOLVES THROUGH THIS SAME CALL, because scanners are keyboard-wedge into
    /// the same box: the server canonicalises a `C…` payload and matches the exact member. Use
    /// <see cref="Plutus.SharedKernel.MemberNumbers.LooksLikeMemberScan"/> to decide whether a scan
    /// belongs here at all rather than in item lookup.
    /// </summary>
    /// <param name="take">Matches the web till's 10 — a picker, not a report.</param>
    public Task<List<CustomerSummaryDto>?> SearchCustomersAsync(
        string? term, int take = 10, CancellationToken ct = default) =>
        GetAsync<List<CustomerSummaryDto>>(
            $"/api/v1/customers?take={take}" +
            (string.IsNullOrWhiteSpace(term) ? "" : $"&search={Uri.EscapeDataString(term)}"), ct);

    /// <summary>
    /// The live read for the customer being attached — balance and membership included.
    ///
    /// ⚠ CALL IT AT ATTACH, EVERY TIME, even when the summary from the search looks sufficient. The
    /// search does not carry the balance or the tier, and the tier is what decides the money.
    /// </summary>
    public Task<CustomerDetailDto?> GetCustomerAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<CustomerDetailDto>($"/api/v1/customers/{id:D}", ct);

    /// <summary>The tenant's active tiers, for the assign-tier picker. Readable by any signed-in
    /// operator; the picker itself is gated `customers.manage` (binding default 20).</summary>
    public Task<List<LoyaltyTierDto>?> GetLoyaltyTiersAsync(CancellationToken ct = default) =>
        GetAsync<List<LoyaltyTierDto>>("/api/v1/loyalty/tiers", ct);

    /// <summary>
    /// Sign a new member up from the till. Returns their id and the membership number just issued.
    ///
    /// ⚠ Needs **`pos.customers.add` OR `customers.manage`** — Cashier and up (binding default 20:
    /// *"Till operator to add new loyalty members"*). **Create-only**: this client deliberately
    /// exposes no customer *edit*, because editing is `customers.manage` and a cashier holding the
    /// add permission must not find an edit call sitting next to it.
    ///
    /// ⚠ ONLINE-ONLY — the number comes from a tenant-wide counter. Failing offline is correct
    /// behaviour, not a gap, and the message says so rather than implying a retry will help.
    /// </summary>
    public async Task<(bool Ok, Guid Id, string? MemberNo, string? Problem)> CreateCustomerAsync(
        string name, string? email = null, string? phone = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (false, Guid.Empty, null, "A name is required to add a member.");

        var body = new Dictionary<string, object?>
        {
            ["name"] = name.Trim(),
            ["email"] = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            ["phone"] = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/customers")
            {
                Content = JsonContent.Create(body, options: Json),
            };
            await AuthoriseAsync(req, ct);
            using var res = await _http.SendAsync(req, ct);

            if (res.IsSuccessStatusCode)
            {
                var created = await res.Content.ReadFromJsonAsync<CreatedCustomer>(Json, ct);
                return (true, created?.Id ?? Guid.Empty, created?.MemberNo, null);
            }

            // ⚠ 403 gets its own sentence. The server's problem body for a permission refusal is not
            // written for a counter, and "Forbidden" in front of a customer tells the operator
            // nothing about what to do next — which is to ask a supervisor.
            if (res.StatusCode == HttpStatusCode.Forbidden)
                return (false, Guid.Empty, null,
                    "You don't have permission to add a member. A supervisor can add them.");

            var detail = await res.Content.ReadAsStringAsync(ct);
            return (false, Guid.Empty, null, string.IsNullOrWhiteSpace(detail)
                ? $"Plutus wouldn't add that member ({(int)res.StatusCode})."
                : detail);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return (false, Guid.Empty, null,
                "Couldn't reach Plutus, so the member has NOT been added. Membership numbers are "
                + "issued centrally, so this needs the till to be online — sell to them now and add "
                + "them when the connection is back.");
        }
    }

    private sealed class CreatedCustomer
    {
        public Guid Id { get; set; }
        public string? MemberNo { get; set; }
    }


    /// <summary>
    /// Everything that has ever happened to one customer — created, details changed, tier set, credit
    /// added, credit used. Newest first.
    ///
    /// ⚠⚠ THE HISTORY IS NOT THE CREDIT LEDGER, and asking for it that way is the mistake this
    /// endpoint exists to stop. Matt, 2026-08-18: *"I need the 'Credit History' to be ALL history."*
    /// The server merges three sources, two of which are not filed under the customer at all.
    ///
    /// ⚠ `take` is clamped 1..200 SERVER-side and `total` counts what MATCHED the search — so a
    /// caller that shows `rows.Count` as the answer is lying whenever the cap bites. Say the cap.
    /// </summary>
    public Task<CustomerHistoryPage?> GetCustomerHistoryAsync(
        Guid id, string? search = null, int skip = 0, int take = 200, CancellationToken ct = default) =>
        GetAsync<CustomerHistoryPage>(
            $"/api/v1/customers/{id:D}/history?skip={skip}&take={take}"
            + (string.IsNullOrWhiteSpace(search) ? "" : $"&search={Uri.EscapeDataString(search)}"), ct);

    /// <summary>One page of a customer's history.</summary>
    public sealed class CustomerHistoryPage
    {
        /// <summary>⚠ What MATCHED, not what was returned — compare with `Rows.Count` and SAY when
        /// the page is short, the same rule as every capped report on this client.</summary>
        public int Total { get; set; }

        public int Skip { get; set; }
        public int Take { get; set; }
        public List<CustomerHistoryRow> Rows { get; set; } = new();
    }

    public sealed class CustomerHistoryRow
    {
        public DateTime AtUtc { get; set; }

        /// <summary>"Created", "Details changed", "Tier set", "Credit added", "Credit used",
        /// "Credit expired" — the SERVER's words, so both tills say the same thing.</summary>
        public string? Type { get; set; }

        /// <summary>The reason, or what changed — e.g. `name: Ada Lovelace -> Ada King`.</summary>
        public string? Detail { get; set; }

        /// <summary>⚠ SIGNED, and only on a credit movement: positive granted, negative spent.
        /// Null on everything else, which is why it is nullable rather than 0 — a rename is not a
        /// zero-pound transaction.</summary>
        public long? AmountPence { get; set; }

        public Guid? ActorUserId { get; set; }
    }
    /// <summary>
    /// Change a member's contact details — name, email, phone.
    ///
    /// ⚠⚠ THIS CLIENT DELIBERATELY HAD NO EDIT UNTIL 2026-08-18, and the comment on
    /// `CreateCustomerAsync` above still records the old reasoning: *"editing is `customers.manage`
    /// and a cashier holding the add permission must not find an edit call sitting next to it."*
    /// The gate argument was sound; the fear behind it was that an email change redirects somebody's
    /// account.
    ///
    /// ⚠⚠ MATT'S RULING, 2026-08-18, settles it: *"A customer needs to have a unique ID, because
    /// people can change emails over time. Audit please."* The id is `Customer.Id` (a UUIDv7) and
    /// **nothing resolves a customer by email**, so an edit cannot redirect an account — and the
    /// server records `before` and `after` on every one. The web till has had this since it was
    /// written (`updateCustomer` in `api.ts`); MAUI was the odd one out.
    ///
    /// ⚠ GATED `customers.manage` SERVER-SIDE, which is stricter than adding a member. A 403 gets
    /// its own sentence, because "Forbidden" in front of a customer tells an operator nothing about
    /// what to do next.
    ///
    /// ⚠ THE TIER IS NOT TOUCHED HERE. `PUT /api/v1/customers/{id}` carries contact details only;
    /// membership is `SetMembershipAsync`. Two calls, two permissions - see `CreateCustomerAsync`'s
    /// 201-then-403 scar for why they must not be pretended to be one.
    /// </summary>
    public async Task<(bool Ok, string? Problem)> UpdateCustomerAsync(
        Guid id, string name, string? email = null, string? phone = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (false, "A name is required.");

        var body = new Dictionary<string, object?>
        {
            ["name"] = name.Trim(),
            ["email"] = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            ["phone"] = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/customers/{id:D}")
            {
                Content = JsonContent.Create(body, options: Json),
            };
            await AuthoriseAsync(req, ct);
            using var res = await _http.SendAsync(req, ct);

            if (res.IsSuccessStatusCode) return (true, null);

            if (res.StatusCode == HttpStatusCode.Forbidden)
                return (false, "You don't have permission to change a member's details. A supervisor can.");

            if (res.StatusCode == HttpStatusCode.NotFound)
                return (false, "That member no longer exists.");

            var detail = await res.Content.ReadAsStringAsync(ct);
            return (false, string.IsNullOrWhiteSpace(detail)
                ? $"Plutus wouldn't save that change ({(int)res.StatusCode})."
                : detail);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return (false, "Couldn't reach Plutus, so nothing has been changed.");
        }
    }


    /// <summary>
    /// Put credit on a customer's account.
    ///
    /// ⚠⚠ SUPERVISOR AND ABOVE, and that was ALREADY the rule rather than a new one. Matt,
    /// 2026-08-18: *"Granting credit needs to be supervisor and above."* The endpoint is gated
    /// `perm:customers.manage`; `RbacSeeder` gives **Supervisor** that code and gives a **Cashier**
    /// only `pos.sell` + `pos.customers.add`. Verified before writing this, not assumed.
    ///
    /// ⚠⚠ A REASON IS MANDATORY AND IS NEVER SUBSTITUTED. The server refuses a blank one
    /// (backend 1.17.4). It used to default to "grant", and the portal sent "goodwill grant" — so
    /// credit could be added with **no reason anybody typed**, and the history then showed a
    /// plausible-looking word that means nothing. **That is worse than a blank, because it reads as an
    /// audit trail.** This refuses locally too, so the operator is told before the round trip.
    ///
    /// ⚠ ONLINE-ONLY, and there is no offline queue for it: the balance is the server's, and a
    /// till that granted credit offline would be inventing money two tills could then both spend.
    ///
    /// ⚠ THE ENTRY ID IS THE CALLER'S, exactly as <see cref="RedeemCreditAsync"/> takes one — it is
    /// the idempotency anchor, so ONE attempt must carry ONE id however many times it is retried.
    /// Minting it in here would defeat that: a retry would look like a second grant and credit the
    /// account twice.
    /// </summary>
    public async Task<(bool Ok, string? Problem)> IssueCreditAsync(
        Guid customerId, long amountPence, string reason, Guid entryId, CancellationToken ct = default)
    {
        if (amountPence <= 0) return (false, "Credit must be more than nothing.");
        if (string.IsNullOrWhiteSpace(reason))
            return (false, "A reason is required, and it is kept on the customer's history.");

        var body = new Dictionary<string, object?>
        {
            ["amountPence"] = amountPence,
            ["reason"] = reason.Trim(),
            ["entryId"] = entryId,
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/customers/{customerId:D}/credit/issue")
            {
                Content = JsonContent.Create(body, options: Json),
            };
            await AuthoriseAsync(req, ct);
            using var res = await _http.SendAsync(req, ct);

            if (res.IsSuccessStatusCode) return (true, null);

            if (res.StatusCode == HttpStatusCode.Forbidden)
                return (false, "You don't have permission to grant credit. A supervisor can.");

            if (res.StatusCode == HttpStatusCode.NotFound)
                return (false, "That customer no longer exists.");

            var detail = await res.Content.ReadAsStringAsync(ct);
            return (false, string.IsNullOrWhiteSpace(detail)
                ? $"Plutus wouldn't add that credit ({(int)res.StatusCode})."
                : detail);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return (false, "Couldn't reach Plutus, so no credit has been added.");
        }
    }
    /// <summary>
    /// Spend a customer's store credit against a sale.
    ///
    /// ⚠⚠ CALL IT **BEFORE** THE SALE IS RECORDED, and abort the sale if it fails — the ordering is
    /// the rule, not a preference. The SERVER owns the balance, so anything it will refuse (an
    /// overdraw, a second attempt) must be refused while the sale can still be abandoned. Redeem
    /// after committing and an overdraw leaves a recorded sale that was never fully paid, with the
    /// customer gone. The web till does exactly this at `api.ts:1090`.
    ///
    /// ⚠ IDEMPOTENT BY <paramref name="entryId"/>, which is what makes a
    /// queued-then-drained sale safe: the same entry id can arrive twice and only draws down once.
    /// **Generate it once per attempt and reuse it on retry** — a fresh id on retry spends the
    /// balance twice.
    ///
    /// ⚠ ONLINE-ONLY, permanently. A till cannot verify a server-held balance offline, which is why
    /// `TillTenders` only offers the button when there is a live balance to spend.
    /// </summary>
    /// <param name="amountPence">Positive pence to draw down.</param>
    public async Task<(bool Ok, string? Problem)> RedeemCreditAsync(
        Guid customerId, long amountPence, Guid saleId, Guid entryId, CancellationToken ct = default)
    {
        if (amountPence <= 0) return (false, "A store-credit payment has to be more than nothing.");

        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Post, $"/api/v1/customers/{customerId:D}/credit/redeem")
            {
                Content = JsonContent.Create(new Dictionary<string, object?>
                {
                    ["amountPence"] = amountPence,
                    ["saleId"] = saleId,
                    ["entryId"] = entryId,
                    // ⚠ The same wording the web till sends, so one report reads consistently
                    // whichever till took the money.
                    ["reason"] = "till sale",
                }, options: Json),
            };

            await AuthoriseAsync(req, ct);
            using var res = await _http.SendAsync(req, ct);
            if (res.IsSuccessStatusCode) return (true, null);

            // ⚠ A 400 here is almost always an OVERDRAW, and the server's own words name the real
            // balance — far more use at a counter than "that didn't work".
            var detail = await res.Content.ReadAsStringAsync(ct);
            return (false, string.IsNullOrWhiteSpace(detail)
                ? $"Plutus wouldn't take that store credit ({(int)res.StatusCode})."
                : detail);
        }
        catch (Exception ex)
        {
            // ⚠ A failure to REACH the server must read as "not taken", never as "probably fine".
            return (false, $"Store credit couldn't be taken: {ex.Message}");
        }
    }

    /// <summary>
    /// Assign a member one of the tenant's tiers.
    ///
    /// ⚠ Needs **`customers.manage`** — Supervisor and up (binding default 20: *"Supervisor to
    /// change tiers"*). A cashier who may add a member may NOT set their tier, because a tier
    /// changes every future basket that customer puts through.
    ///
    /// ⚠ The TIER owns the discount and the renewal length. A till passes an id and nothing else —
    /// it never types a name or a rate, so re-rating "Gold" in the portal updates every Gold member
    /// at once instead of leaving a snapshot behind on whichever till happened to assign it.
    /// </summary>
    public async Task<(bool Ok, string? Problem)> SetMembershipAsync(
        Guid customerId, Guid tierId, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Post, $"/api/v1/customers/{customerId:D}/membership")
            {
                Content = JsonContent.Create(new Dictionary<string, object?> { ["tierId"] = tierId }, options: Json),
            };
            await AuthoriseAsync(req, ct);
            using var res = await _http.SendAsync(req, ct);
            if (res.IsSuccessStatusCode) return (true, null);

            if (res.StatusCode == HttpStatusCode.Forbidden)
                return (false, "You don't have permission to change a member's tier — that needs a supervisor.");

            // ⚠ A 400 here is usually "that tier is no longer active", which is the server's own
            // words and the actionable sentence: deactivating a tier stops new assignments while
            // leaving existing members working.
            var detail = await res.Content.ReadAsStringAsync(ct);
            return (false, string.IsNullOrWhiteSpace(detail)
                ? $"Plutus wouldn't set that tier ({(int)res.StatusCode})."
                : detail);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return (false, "Couldn't reach Plutus. The tier has NOT been changed.");
        }
    }

    /// <summary>
    /// The loyalty list — everyone who is a member **or** holds store credit, with their tier,
    /// auto-discount, renewal and live balance.
    ///
    /// ⚠ IT IS A LOOKUP, NOT A CONFIGURATION SCREEN. Tiers are created in the portal only (binding
    /// default 20); a till reads this to answer *"what does this customer have?"* away from a sale.
    ///
    /// ⚠ The server orders by balance then name and clamps `take` to 1–500. The default of 200
    /// matches the web till's, so the two show the same page of the same list.
    /// </summary>
    public async Task<List<LoyaltyRowDto>?> GetLoyaltyAsync(
        string? search = null, int take = 200, CancellationToken ct = default)
    {
        var url = $"/api/v1/loyalty?take={take}" +
                  (string.IsNullOrWhiteSpace(search) ? "" : $"&search={Uri.EscapeDataString(search)}");

        // ⚠ The payload is `{ count, rows }`, not a bare array — reading it as an array silently
        // yields nothing, which looks exactly like "this tenant has no members".
        var page = await GetAsync<LoyaltyPageDto>(url, ct);
        return page?.Rows;
    }

    private sealed class LoyaltyPageDto
    {
        public int Count { get; set; }
        public List<LoyaltyRowDto>? Rows { get; set; }
    }

    /// <summary>One row of the loyalty list.</summary>
    public sealed class LoyaltyRowDto
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? MemberNo { get; set; }
        public Guid? TierId { get; set; }
        public string? Tier { get; set; }

        /// <summary>⚠ The TIER's current rate, not a snapshot taken when it was assigned — re-rating
        /// "Gold" in the portal moves every Gold member at once.</summary>
        public decimal? AutoDiscountRate { get; set; }

        public string? RenewalDay { get; set; }

        /// <summary>⚠ The SERVER's verdict, never re-derived from <see cref="RenewalDay"/> against a
        /// till's clock — one clock decides, or two tills disagree about who is entitled.</summary>
        public bool Expired { get; set; }

        public long CreditBalancePence { get; set; }

        /// <summary>
        /// The day they joined, `yyyy-MM-dd`, as the SERVER formatted it.
        ///
        /// ⚠ A STRING, NOT A `DateTime`, on purpose - the same choice as `RenewalDay` right above.
        /// One clock decides what day something happened on, and a till re-formatting an instant
        /// against its own culture and timezone is how two tills come to print different dates for the
        /// same customer.
        /// </summary>
        public string? CreatedAtUtc { get; set; }
    }

    // ── reporting (WP11 / step 26) ─────────────────────────────────────────────────────────────
    //
    // ⚠⚠ THE UNIT IS IN THE TYPE NAME. `summary-rich` answers in POUNDS; every other report answers
    // in PENCE. See `ReportContracts.cs` — two endpoints on one screen, one a hundred times the
    // other, and the JSON gives no clue which is which.
    //
    // ⚠ MAUI's Statistics tab read LOCAL SQLITE and has therefore reported ZERO for everything sold
    // since cutover step 11, when sales stopped being written there. These replace it. **No report
    // reads the local database.**

    /// <summary>
    /// The till's own Summary — takings, orders, top items, tenders and VAT bands for a date range.
    /// ⚠⚠ **POUNDS.** Uniquely among the reports; see the type name.
    /// </summary>
    public Task<SalesSummaryPounds?> GetSalesSummaryAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default) =>
        GetAsync<SalesSummaryPounds>(
            $"/api/v1/reports/summary-rich?from={Day(from)}&to={Day(to)}", ct);

    // ⚠ THERE IS NO SECOND `GetReportSummaryAsync` HERE, AND THERE WAS BRIEFLY. I added an
    // `(int storeId, …)` overload on 2026-08-16 without noticing the `(string level, string id, …)`
    // one further up already answered exactly that question — two ways to ask one thing, and the
    // catalogue never called mine. Deleted rather than kept: a duplicate that nothing uses is how a
    // codebase grows two answers that later disagree. **Use the `level`/`id` method above.**

    /// <summary>The VAT table. **PENCE.** ⚠ Defaults to MONTH buckets, matching the web till — VAT
    /// is returned monthly, and a daily VAT table is a different question nobody asked.</summary>
    public Task<ReportVat?> GetReportVatAsync(
        int storeId, DateOnly from, DateOnly to, string granularity = "month", CancellationToken ct = default) =>
        GetAsync<ReportVat>(
            $"/api/v1/reports/vat?level=store&id={storeId}&from={Day(from)}&to={Day(to)}&granularity={granularity}", ct);

    /// <summary>
    /// Every line sold in a range. **PENCE.**
    ///
    /// ⚠⚠ THE SERVER CAPS THIS AT <see cref="ItemsSoldRowCap"/> ROWS AND THE CAP IS SILENT. A busy
    /// fortnight exceeds it, and the report then shows a total that is quietly short. The caller
    /// MUST surface it — see `count` against the rows returned.
    /// </summary>
    public Task<ItemsSold?> GetItemsSoldAsync(
        int storeId, DateOnly from, DateOnly to, Guid? operatorUserId = null, CancellationToken ct = default) =>
        GetAsync<ItemsSold>(
            $"/api/v1/reports/items-sold?from={Day(from)}&to={Day(to)}&storeId={storeId}&take={ItemsSoldRowCap}" +
            (operatorUserId is Guid op ? $"&operatorUserId={op:D}" : ""), ct);

    /// <summary>⚠ The row cap `items-sold` is requested with — matching the web till's 2000, so the
    /// two truncate at the same point rather than disagreeing about a total.</summary>
    public const int ItemsSoldRowCap = 2000;

    /// <summary>Sales by category — a report MAUI has never had. **PENCE.**</summary>
    public Task<CategorySales?> GetCategorySalesAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default) =>
        GetAsync<CategorySales>(
            $"/api/v1/reports/category-sales?from={Day(from)}&to={Day(to)}", ct);

    /// <summary>Best sellers — also new to MAUI. **PENCE.**</summary>
    /// <param name="by">`qty` or `gross`. ⚠ They rank differently and both are legitimate: a shop's
    /// best seller by units is rarely its best by money.</param>
    public Task<BestSellers?> GetBestSellersAsync(
        DateOnly from, DateOnly to, string by = "qty", int take = 25, CancellationToken ct = default) =>
        GetAsync<BestSellers>(
            $"/api/v1/reports/best-sellers?from={Day(from)}&to={Day(to)}&by={by}&take={take}", ct);

    /// <summary>
    /// On-hand stock, a page at a time — the till's **Stock** report, and with
    /// `filter: "negative"` its **Negative stock** report. ⚠ **QUANTITIES, NOT PENCE** — the only
    /// report on this client that is not money.
    ///
    /// ⚠ ONE ENDPOINT, TWO REPORTS, and that is the web till's shape too (`StockView negativeOnly`).
    /// A second endpoint for "the same rows where quantity &lt; 0" would be two things to keep
    /// agreeing about what a stock row is.
    ///
    /// ⚠ THE SERVER CLAMPS `take` TO 200 and answers `matched` with the true count, so the caller
    /// must compare the two and SAY when the page is short. Silence there is a stock report that
    /// looks complete and is not.
    ///
    /// ⚠ Needs an OPERATOR token — `portal.reports.view` OR `pos.reports.view`. ⚠⚠ The `pos.*`
    /// alternative was **missing until 2026-08-18**, so this returned 403 for every Supervisor and
    /// Cashier: the fourth time that same gate defect has been fixed, and the first three did not
    /// reach this endpoint even though one of them fixed its own sibling six lines above it.
    /// </summary>
    public Task<StockLevelsPage?> GetStockLevelsPageAsync(
        string? search = null, string? filter = null, int skip = 0, int take = StockRowCap,
        CancellationToken ct = default) =>
        GetAsync<StockLevelsPage>(
            $"/api/v1/stock/levels?skip={skip}&take={take}"
            // ⚠ ESCAPED. An item code with an ampersand or a space in it would otherwise truncate
            // the query string and silently return the WRONG page rather than failing.
            + (string.IsNullOrWhiteSpace(search) ? "" : $"&search={Uri.EscapeDataString(search)}")
            + (string.IsNullOrWhiteSpace(filter) ? "" : $"&filter={Uri.EscapeDataString(filter)}"), ct);

    /// <summary>⚠ The server's own clamp (`Math.Clamp(take, 1, 200)`). Asking for more would not
    /// fail — it would quietly return 200 and a `matched` the caller might not check.</summary>
    public const int StockRowCap = 200;

    /// <summary>⚠ `yyyy-MM-dd`, the only format these endpoints accept — and INVARIANT, because a
    /// till in a culture that formats dates differently would silently query the wrong range.</summary>
    private static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    // ── gift cards (WP13) ──────────────────────────────────────────────────────────────────────
    //
    // ⚠⚠ ALL THREE NEED CONNECTIVITY AND THERE IS NO OFFLINE QUEUE FOR THEM, deliberately. The
    // SERVER is the balance authority; a card redeemed twice offline is money given away, and one
    // activated twice is stored value created out of nothing. Ordinary sales stay offline-capable —
    // these do not, and that is a property of the feature rather than a gap in it.

    /// <summary>
    /// What a scanned card is — balance, status and the tenant's VAT treatment.
    ///
    /// ⚠ 404 on an unknown or mis-keyed code, which is an ordinary outcome at a counter and returns
    /// null rather than throwing.
    /// </summary>
    public Task<GiftCardLookupDto?> LookupGiftCardAsync(string code, CancellationToken ct = default) =>
        GetAsync<GiftCardLookupDto>($"/api/v1/giftcards/{Uri.EscapeDataString(code ?? string.Empty)}/lookup", ct);

    /// <summary>
    /// SELL a card: load it with <paramref name="amountPence"/>.
    ///
    /// ⚠ CALL IT BEFORE THE SALE IS RECORDED and abort if it fails — a card that cannot be loaded
    /// (already active, for instance) must stop the sale BEFORE the customer is charged for it.
    /// ⚠ Idempotent by <paramref name="entryId"/>; **409 if the card is already active**.
    /// </summary>
    public Task<(bool Ok, long BalancePence, string? Problem)> ActivateGiftCardAsync(
        string code, long amountPence, Guid saleId, Guid entryId,
        Guid? customerId = null, CancellationToken ct = default) =>
        GiftCardPostAsync(code, "activate", new Dictionary<string, object?>
        {
            ["amountPence"] = amountPence,
            ["saleId"] = saleId,
            ["entryId"] = entryId,
            ["customerId"] = customerId,
        }, ct);

    /// <summary>
    /// SPEND a card against a sale.
    ///
    /// ⚠ Same ordering rule as store credit and for the same reason: the server owns the balance, so
    /// anything it will refuse must be refused while the sale can still be abandoned.
    /// ⚠ Idempotent by <paramref name="entryId"/>; **409 on over-redeem, expired, void or unsold**.
    /// </summary>
    public Task<(bool Ok, long BalancePence, string? Problem)> RedeemGiftCardAsync(
        string code, long amountPence, Guid saleId, Guid entryId, CancellationToken ct = default) =>
        GiftCardPostAsync(code, "redeem", new Dictionary<string, object?>
        {
            ["amountPence"] = amountPence,
            ["saleId"] = saleId,
            ["entryId"] = entryId,
        }, ct);

    /// <summary>
    /// A gift-card write.
    ///
    /// ⚠⚠ IT SURFACES THE SERVER'S OWN `detail`, unlike every other write on this client. A gift-card
    /// refusal is a sentence the CASHIER has to read and act on — *"that card only has £12.50 left"* —
    /// not a status code to swallow. The web till makes the same exception (`giftCardPost`) and for
    /// the same reason.
    ///
    /// ⚠ A 409 WITH NO SETTINGS MEANS THE TENANT HAS NOT CHOSEN A VAT TREATMENT. `GiftCardSettings`'
    /// absence 409s generate, activate AND redeem — by design, because the treatment decides when VAT
    /// falls due and guessing it would put a wrong number on a VAT return. The message says so, or a
    /// cashier is left reading "conflict" at a counter.
    /// </summary>
    private async Task<(bool Ok, long BalancePence, string? Problem)> GiftCardPostAsync(
        string code, string action, Dictionary<string, object?> body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            return (false, 0, "No gift-card number was given.");

        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Post, $"/api/v1/giftcards/{Uri.EscapeDataString(code)}/{action}")
            {
                Content = JsonContent.Create(body, options: Json),
            };

            await AuthoriseAsync(req, ct);
            using var res = await _http.SendAsync(req, ct);

            if (res.IsSuccessStatusCode)
            {
                var ok = await res.Content.ReadFromJsonAsync<GiftCardWriteResultDto>(Json, ct);
                return (true, ok?.BalancePence ?? 0, null);
            }

            var detail = await res.Content.ReadAsStringAsync(ct);

            // ⚠ The server sends ProblemDetails; pull `detail` out of it rather than showing an
            // operator a JSON blob. Falling back to the raw body is deliberate — an unexpected shape
            // is still more use than "something went wrong".
            var message = TryReadProblemDetail(detail) ?? detail;

            if (string.IsNullOrWhiteSpace(message))
                message = $"Plutus wouldn't {action} that gift card ({(int)res.StatusCode}).";

            return (false, 0, message);
        }
        catch (Exception ex)
        {
            // ⚠ Unreachable must read as "not done", never as "probably fine".
            return (false, 0, $"That gift card couldn't be reached: {ex.Message}");
        }
    }

    private static string? TryReadProblemDetail(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("detail", out var d) ? d.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>What a gift-card write answers with.</summary>
    public sealed class GiftCardWriteResultDto
    {
        public Guid EntryId { get; set; }
        public string? Code { get; set; }
        public long BalancePence { get; set; }
    }

    /// <summary>
    /// A gift card as the till needs to see it.
    /// </summary>
    /// <remarks>
    /// ⚠ <see cref="VatTreatment"/> IS NOT COSMETIC — it decides WHEN the card's VAT falls due, and
    /// the two answers are genuinely different taxes at different moments:
    ///   • <b>multi</b> (the tenant sells mixed rates): **no VAT when the card is sold** — it is a
    ///     liability, not a supply — and VAT comes off the goods when the card is SPENT.
    ///   • <b>single</b> (everything one rate): VAT is charged when the card is **SOLD**, and a
    ///     redemption then reduces the sale's VAT-able total rather than acting as a plain tender.
    /// Guessing it puts a wrong figure on a VAT return, which is why the server 409s rather than
    /// defaulting when the tenant has not chosen.
    /// </remarks>
    public sealed class GiftCardLookupDto
    {
        public string? Code { get; set; }

        /// <summary>Grouped for reading aloud — "K7QP-2M9W-XT4R-8".</summary>
        public string? Pretty { get; set; }

        public long BalancePence { get; set; }

        /// <summary>unsold · active · spent · expired · void</summary>
        public string? Status { get; set; }

        public DateTime? ExpiresAtUtc { get; set; }
        public Guid? CustomerId { get; set; }

        /// <summary>The catalogue row an activation is rung through — stock-untracked. ⚠ Compare it
        /// with <see cref="Plutus.SharedKernel.GiftCards.ItemIdOne"/>, never a literal.</summary>
        public string? ItemIdOne { get; set; }

        /// <summary>"multi" or "single" — see the remarks on this class.</summary>
        public string? VatTreatment { get; set; }

        /// <summary>⚠ Can this card be SPENT right now? Only an `active` card with money on it.
        /// `unsold` is the trap: a card on the rack has a code and looks real, and taking it as
        /// payment would give away goods against value nobody ever bought.</summary>
        public bool IsSpendable =>
            string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase) && BalancePence > 0;
    }

    /// <summary>
    /// GET and deserialise, or hand back `default` — **never throw at a screen**.
    ///
    /// ⚠⚠ IT USED TO THROW, and that was a real hole rather than a style point. `ReadFromJsonAsync`
    /// raises `JsonException` when the server's answer is not the shape asked for, and nothing here
    /// caught it — so every caller inherited an exception path none of them handled. It stayed hidden
    /// because the reports that use this all deserialise OBJECTS, and the empty object `{}` is valid
    /// for an object; the first caller to ask for a JSON **array** (`GET /api/v1/sales`, the sale
    /// drill-down, 2026-08-18) threw immediately, and it was `No_report_throws_when_the_server_
    /// answers_with_nonsense` — a test written for exactly this — that caught it.
    ///
    /// ⚠ `GetStockLevelsAsync` has carried this same catch **by hand** since it was written, which
    /// means somebody hit this before and fixed their own call site instead of the helper. Same
    /// failure mode as §5c item 7 and as the `pos.reports.view` gate: a local fix teaches the next
    /// caller nothing. It is now in the one place every GET goes through.
    ///
    /// ⚠ NULL MEANS "COULD NOT READ", and every caller already treats it that way — a report says so
    /// on screen rather than rendering £0.00, which would tell an operator the shop sold nothing.
    ///
    /// ⚠⚠ **ONLY THE DESERIALISATION FAULTS ARE SWALLOWED. TRANSPORT EXCEPTIONS STILL PROPAGATE**,
    /// and that is not caution — it is a correction. Catching `HttpRequestException` here as well
    /// broke `SyncClientTests.A_dead_network_is_an_outcome_not_an_exception`, whose whole point is
    /// that a dead line must be an outcome **carrying its reason**: the catalogue sync puts the
    /// exception's message into `outcome.Error`, so swallowing it turned *"No such host is known"*
    /// into a bare *"the catalogue feed did not answer"*. A till on a flaky line then cannot tell DNS
    /// from a refused connection from a wrong URL. ⚠ The test caught me widening this too far; the
    /// narrower catch fixes the hole above and changes nothing else.
    ///
    /// ⚠ So a caller that needs to explain a NETWORK failure still can, and a caller that only needs
    /// to know "is there data?" still gets null for a malformed answer. Those are different questions
    /// and the split is deliberate.
    /// </summary>
    // ── WP10: an item's additional barcodes, and its change history ─────────────────────────────
    //
    // ⚠⚠ MAUI HAD NONE OF THIS. The endpoints shipped 2026-08-19/20 with the portal and the web till
    // both wired to them, and MAUI's item editor — which does exist, contrary to what six places in
    // `MAUI-retrofit.md` claimed — had no barcode section and no history. This is the client half.

    /// <summary>Every additional barcode this item answers to, in code order.</summary>
    /// <remarks>
    /// ⚠ The server's list endpoint returns the WHOLE TENANT'S codes, deliberately: the portal and the
    /// web till use it to answer "is this code taken" with no round trip per keystroke. Filtering to
    /// one item happens here so callers do not each re-derive it.
    /// </remarks>
    public async Task<List<ItemBarcodeDto>> GetItemBarcodesAsync(
        string itemIdOne, CancellationToken ct = default)
    {
        var all = await GetAsync<List<ItemBarcodeDto>>("/api/v1/items/barcodes", ct);
        if (all is null) return new List<ItemBarcodeDto>();

        return all
            .Where(b => string.Equals(b.ItemIdOne, itemIdOne, StringComparison.OrdinalIgnoreCase))
            .OrderBy(b => b.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Every barcode in the tenant — for the "is this code free" check with no round trip.</summary>
    public async Task<List<ItemBarcodeDto>> GetAllItemBarcodesAsync(CancellationToken ct = default) =>
        await GetAsync<List<ItemBarcodeDto>>("/api/v1/items/barcodes", ct) ?? new List<ItemBarcodeDto>();

    /// <summary>
    /// Give an item another barcode.
    ///
    /// ⚠⚠ RETURNS THE SERVER'S SENTENCE, NOT A BOOL. Every refusal here is written to be shown to a
    /// person verbatim — "That is the shape of a membership card…", "That code belongs to &lt;item&gt;"
    /// — and the reserved shapes live ONLY in `SharedKernel.ItemBarcodeRules`. A client that reduced
    /// this to true/false would have to invent its own wording, which is exactly the C2 fault: a copy
    /// of an identity rule in a client.
    ///
    /// ⚠ Re-adding the same code to the same item is a 204 NO-OP server-side, not a conflict — a retry
    /// after a dropped response must not read as an error.
    /// </summary>
    public Task<ItemBarcodeOutcome> AddItemBarcodeAsync(
        string itemIdOne, string code, CancellationToken ct = default) =>
        BarcodeWriteAsync(HttpMethod.Post,
            $"/api/v1/items/{Uri.EscapeDataString(itemIdOne)}/barcodes",
            new ItemBarcodeBody(code), ct);

    /// <summary>
    /// Correct a barcode — ONE call, never delete-then-add.
    ///
    /// ⚠⚠ THE REASON IS THE WHOLE POINT (backend 1.20.0). Two calls can fail between them and leave
    /// the item with NEITHER code, and for a barcode that means an item that silently stops scanning.
    /// </summary>
    public Task<ItemBarcodeOutcome> RenameItemBarcodeAsync(
        string itemIdOne, string oldCode, string newCode, CancellationToken ct = default) =>
        BarcodeWriteAsync(HttpMethod.Put,
            $"/api/v1/items/{Uri.EscapeDataString(itemIdOne)}/barcodes/{Uri.EscapeDataString(oldCode)}",
            new ItemBarcodeBody(newCode), ct);

    /// <summary>Take a barcode off an item. ⚠ The item keeps its own `IdOne`, which is never a
    /// removable alias — the server refuses that and says so.</summary>
    public Task<ItemBarcodeOutcome> RemoveItemBarcodeAsync(
        string itemIdOne, string code, CancellationToken ct = default) =>
        BarcodeWriteAsync(HttpMethod.Delete,
            $"/api/v1/items/{Uri.EscapeDataString(itemIdOne)}/barcodes/{Uri.EscapeDataString(code)}",
            null, ct);

    /// <summary>
    /// One barcode write, and the server's own words when it refuses.
    ///
    /// ⚠ A TRANSPORT FAILURE IS NOT A REFUSAL and must not be shown as one — "that code belongs to
    /// another item" and "the network dropped" call for different actions from the person holding the
    /// scanner. `Ok=false` with a NULL `Problem` means the request never landed.
    /// </summary>
    private async Task<ItemBarcodeOutcome> BarcodeWriteAsync(
        HttpMethod method, string url, ItemBarcodeBody? body, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(method, url);
            if (body is not null) req.Content = JsonContent.Create(body, options: Json);
            await AuthoriseAsync(req, ct);

            using var res = await _http.SendAsync(req, ct);
            if (res.IsSuccessStatusCode) return new ItemBarcodeOutcome(true, null);

            // ⚠ The sentence is `detail` on a ProblemDetails body. Read it, and fall back to something
            // honest rather than a status code nobody at a counter can act on.
            string? problem = null;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(
                    await res.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.TryGetProperty("detail", out var d)) problem = d.GetString();
            }
            catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException)
            {
                // Not a ProblemDetails body. The fallback sentence below is still true.
            }

            return new ItemBarcodeOutcome(false,
                string.IsNullOrWhiteSpace(problem) ? "Plutus refused that barcode." : problem);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // ⚠ NULL problem = it never landed. The caller says "check the connection", not "refused".
            return new ItemBarcodeOutcome(false, null);
        }
    }

    /// <summary>
    /// What has happened to this item — price and detail edits, barcode changes, AND stock movements.
    ///
    /// ⚠ Gated `perm:portal.reports.view,pos.reports.view`. A **Cashier holds neither**, which is the
    /// 2026-08-20 ruling: naming who changed a price is a supervisory record, not a stock task.
    /// </summary>
    public Task<ItemHistoryPage?> GetItemHistoryAsync(
        string itemIdOne, int take = 100, CancellationToken ct = default) =>
        GetAsync<ItemHistoryPage>(
            $"/api/v1/items/{Uri.EscapeDataString(itemIdOne)}/history?take={take}", ct);

    /// <summary>One of an item's additional barcodes.</summary>
    public sealed class ItemBarcodeDto
    {
        public string? Code { get; set; }
        public string? ItemIdOne { get; set; }
    }

    private sealed record ItemBarcodeBody(string Code);

    /// <param name="Ok">The write landed.</param>
    /// <param name="Problem">⚠ The server's own sentence, to be shown VERBATIM. Null when the request
    /// never landed at all — a different thing, needing a different message.</param>
    public sealed record ItemBarcodeOutcome(bool Ok, string? Problem);

    /// <summary>An item's history, newest first.</summary>
    public sealed class ItemHistoryPage
    {
        /// <summary>⚠ What EXISTS, not what was returned — say so when the page is short.</summary>
        public int Total { get; set; }
        public List<ItemHistoryRow> Rows { get; set; } = new();
    }

    public sealed class ItemHistoryRow
    {
        public DateTime AtUtc { get; set; }

        /// <summary>The SERVER's words, so both tills say the same thing: "Created",
        /// "Details changed", "Barcode added", "Stock received", "Stock written off"…</summary>
        public string? Type { get; set; }

        public string? Detail { get; set; }

        /// <summary>The person's name, or their id when the roster no longer knows them — never
        /// blank. Staff leave, and a trail that renders "(unknown)" answers less than one that
        /// says who it was.</summary>
        public string? By { get; set; }
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        await AuthoriseAsync(req, ct);
        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return default;

        try
        {
            return await res.Content.ReadFromJsonAsync<T>(Json, ct);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            // ⚠ The server answered, and the answer was not the shape we asked for. That is not a
            // network problem and must not be reported as one — it is "no data", which is what every
            // caller's null branch already says.
            return default;
        }
    }

    /// <summary>POST a JSON body where the CALLER only needs to know whether it worked.</summary>
    private async Task<bool> PostJsonAsync<T>(string url, T body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body, options: Json),
        };
        await AuthoriseAsync(req, ct);
        using var res = await _http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }

    private async Task AuthoriseAsync(HttpRequestMessage req, CancellationToken ct)
    {
        if (_tokens == null) return;
        var token = await _tokens.GetAccessTokenAsync(ct);
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }
}
