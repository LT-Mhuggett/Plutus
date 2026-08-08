using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// The till's "am I connected?" answer.
///
/// Every test here pins the SAME point from a different angle: <b>"offline" is three faults, and
/// telling them apart is the entire value of the feature.</b> A till that says "offline" when its
/// device credential was revoked sends a shop to reboot a router for an afternoon; a till that says
/// "offline" when the shop's broadband is down sends someone to ring support about a server that is
/// perfectly healthy. Both have happened to other POS systems and both are pure diagnosis failures.
/// </summary>
public class ConnectivityProbeTests
{
    // ── test doubles ──

    private sealed class FakeNetwork : INetworkAvailability
    {
        public bool HasNetwork { get; set; } = true;
    }

    private sealed class FakeCredentials : IDeviceCredentialStore
    {
        public Guid? DeviceId { get; private set; }
        public string? ClientSecret { get; private set; }
        public void Save(Guid deviceId, string clientSecret) { DeviceId = deviceId; ClientSecret = clientSecret; }
        public void Clear() { DeviceId = null; ClientSecret = null; }
        public static FakeCredentials Enrolled()
        {
            var c = new FakeCredentials();
            c.Save(Guid.NewGuid(), "secret");
            return c;
        }
    }

    /// <summary>Answers per PATH, so a test can make the ping succeed and the device check fail —
    /// which is the whole "reachable but rejected" case.</summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        public HttpStatusCode PingStatus = HttpStatusCode.OK;
        public HttpStatusCode DeviceStatusCode = HttpStatusCode.OK;
        public string DeviceState = "Active";
        public Exception? PingThrows;
        public bool PingReturnsHtml;
        public TimeSpan PingDelay = TimeSpan.Zero;
        public DateTime ServerUtcNow = DateTime.UtcNow;
        public readonly List<string> Paths = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);

            if (path.Contains("/ping"))
            {
                if (PingThrows != null) throw PingThrows;
                if (PingDelay > TimeSpan.Zero) await Task.Delay(PingDelay, ct);
                if (PingStatus != HttpStatusCode.OK) return new HttpResponseMessage(PingStatus);
                if (PingReturnsHtml)
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("<html>Sign in to WiFi</html>", System.Text.Encoding.UTF8, "text/html"),
                    };
                return Json(HttpStatusCode.OK,
                    $"{{\"ok\":true,\"utcNow\":\"{ServerUtcNow:yyyy-MM-ddTHH:mm:ss.fffffff}Z\",\"apiVersion\":\"1.2.3\"}}");
            }

            if (path.Contains("/status"))
            {
                if (DeviceStatusCode != HttpStatusCode.OK) return new HttpResponseMessage(DeviceStatusCode);
                return Json(HttpStatusCode.OK, $"{{\"status\":\"{DeviceState}\"}}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }

    private static ConnectivityProbe Probe(
        RoutingHandler handler,
        IDeviceCredentialStore? credentials = null,
        INetworkAvailability? network = null,
        DateTime? tillNow = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://plutus.example") };
        return new ConnectivityProbe(
            new PlutusApiClient(http), credentials, network,
            utcNow: tillNow is DateTime t ? () => t : null);
    }

    // ── the three faults, told apart ──

    [Fact]
    public async Task No_network_reports_NoNetwork_and_never_calls_the_server()
    {
        var handler = new RoutingHandler();
        var status = await Probe(handler, network: new FakeNetwork { HasNetwork = false }).CheckAsync();

        Assert.Equal(TillConnection.NoNetwork, status.State);
        Assert.False(status.ServerReachable);
        // The point: no 5-second timeout on a screen in a shop whose router is dead.
        Assert.Empty(handler.Paths);
    }

    [Fact]
    public async Task Network_up_but_nothing_answers_reports_NoServer()
    {
        var handler = new RoutingHandler { PingThrows = new HttpRequestException("No such host is known.") };
        var status = await Probe(handler, network: new FakeNetwork()).CheckAsync();

        Assert.Equal(TillConnection.NoServer, status.State);
        Assert.False(status.ServerReachable);
        Assert.Contains("No such host", status.Detail);
    }

    [Fact]
    public async Task An_OLDER_backend_with_no_ping_endpoint_still_reads_as_REACHABLE()
    {
        // ⚠ THE ROLLOUT BUG THIS PREVENTS. /api/v1/ping is new. A till pointed at a backend that
        // predates it gets a 404 — and a 404 is proof the server IS there. Reading it as "offline"
        // would report every shop in the estate as down for the whole rollout window (retrofit
        // risk #7, mixed versions) and send people to check cables that were never the problem.
        var handler = new RoutingHandler { PingStatus = HttpStatusCode.NotFound };
        var status = await Probe(handler, FakeCredentials.Enrolled()).CheckAsync();

        Assert.True(status.ServerReachable);
        Assert.Equal(TillConnection.Online, status.State);
    }

    [Fact]
    public async Task A_sick_server_is_reachable_not_absent()
    {
        // A 502 is a server having a bad day, not a missing one. "Check your cable" is the wrong
        // advice and wastes the one person who could have rung support instead.
        var handler = new RoutingHandler { PingStatus = HttpStatusCode.BadGateway };
        var status = await Probe(handler, FakeCredentials.Enrolled()).CheckAsync();

        Assert.True(status.ServerReachable);
    }

    [Fact]
    public async Task A_captive_portal_answering_with_HTML_is_NOT_reachable()
    {
        // Shop wifi with a "click here to accept" page in the way. Something answered, but it was
        // not Plutus — and saying "connected" here would be the most misleading answer of all.
        var handler = new RoutingHandler { PingReturnsHtml = true };
        var status = await Probe(handler).CheckAsync();

        Assert.Equal(TillConnection.NoServer, status.State);
        Assert.Contains("not Plutus", status.Detail);
    }

    [Fact]
    public async Task A_revoked_till_is_Rejected_NOT_offline()
    {
        // ⚠ THE REGRESSION THIS EXISTS TO PREVENT. The network is provably fine — the ping
        // succeeded. Reporting this as "offline" is what sends a shop to reboot its router while
        // the actual fix is one click in the portal.
        var handler = new RoutingHandler { DeviceState = "Revoked" };
        var status = await Probe(handler, FakeCredentials.Enrolled()).CheckAsync();

        Assert.Equal(TillConnection.Rejected, status.State);
        Assert.True(status.ServerReachable);
        Assert.False(status.IsOnline);
        Assert.Contains("revoked", status.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_refused_credential_is_also_Rejected()
    {
        var handler = new RoutingHandler { DeviceStatusCode = HttpStatusCode.Unauthorized };
        var status = await Probe(handler, FakeCredentials.Enrolled()).CheckAsync();

        Assert.Equal(TillConnection.Rejected, status.State);
        Assert.True(status.ServerReachable);
    }

    [Fact]
    public async Task PendingRemoval_keeps_trading()
    {
        // ⚠ A removal REQUEST is not a stop signal. Halting the till the moment someone asks for it
        // back would turn un-enrolment into a way to take a shop's till down — so this is Online,
        // flagged, and the sale goes through.
        var handler = new RoutingHandler { DeviceState = "PendingRemoval" };
        var status = await Probe(handler, FakeCredentials.Enrolled()).CheckAsync();

        Assert.Equal(TillConnection.Online, status.State);
        Assert.True(status.IsOnline);
        Assert.True(status.PendingRemoval);
    }

    [Fact]
    public async Task The_probe_never_mints_a_device_token()
    {
        // ⚠ POST /api/v1/tokens/device is rate-limited to 5/min per IP. A probe that minted on
        // every check would 429 itself into reporting a healthy till as revoked — and in a shop
        // where several tills share one public IP, they would do it to each other.
        var handler = new RoutingHandler();
        await Probe(handler, FakeCredentials.Enrolled()).CheckAsync();

        Assert.DoesNotContain(handler.Paths, p => p.Contains("/tokens/device"));
    }

    [Fact]
    public async Task A_fresh_install_is_NotEnrolled_not_Rejected()
    {
        // A till that has never enrolled has done nothing wrong, and telling its operator the
        // device was "revoked" on first run is a support call for a working machine.
        var status = await Probe(new RoutingHandler(), new FakeCredentials()).CheckAsync();

        Assert.Equal(TillConnection.NotEnrolled, status.State);
        Assert.True(status.ServerReachable);
        Assert.False(status.IsOnline);
    }

    [Fact]
    public async Task An_enrolled_till_against_a_healthy_server_is_Online()
    {
        var handler = new RoutingHandler();
        var status = await Probe(handler, FakeCredentials.Enrolled()).CheckAsync();

        Assert.Equal(TillConnection.Online, status.State);
        Assert.True(status.IsOnline);
        Assert.Contains("1.2.3", status.Detail);
        // Both questions were actually asked.
        Assert.Contains(handler.Paths, p => p.Contains("/ping"));
        Assert.Contains(handler.Paths, p => p.Contains("/status"));
    }

    [Fact]
    public async Task Identity_check_can_be_skipped_for_first_run_setup()
    {
        // While the operator is still typing a server URL there is no credential to check, and
        // "not enrolled" would be noise on a screen whose only question is "is that address right?"
        var handler = new RoutingHandler();
        var status = await Probe(handler).CheckAsync(verifyIdentity: false);

        Assert.Equal(TillConnection.Online, status.State);
        Assert.DoesNotContain(handler.Paths, p => p.Contains("/status"));
    }

    // ── the clock ──

    [Fact]
    public async Task A_drifted_till_clock_is_flagged()
    {
        // Device tokens expire and VAT bands are effective-dated, so a till an hour out can have a
        // day's takings judged against the wrong instant — silently.
        var tillNow = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);
        var handler = new RoutingHandler { ServerUtcNow = tillNow.AddHours(1) };
        var status = await Probe(handler, FakeCredentials.Enrolled(), tillNow: tillNow).CheckAsync();

        Assert.Equal(TillConnection.Online, status.State);
        Assert.True(status.ClockSuspect);
        Assert.Equal(TimeSpan.FromHours(1), status.ClockSkew);
    }

    [Fact]
    public async Task A_healthy_clock_is_not_flagged()
    {
        var tillNow = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);
        var handler = new RoutingHandler { ServerUtcNow = tillNow.AddSeconds(3) };
        var status = await Probe(handler, FakeCredentials.Enrolled(), tillNow: tillNow).CheckAsync();

        Assert.False(status.ClockSuspect);
    }

    // ── bounded, always ──

    [Fact]
    public async Task A_hanging_server_is_bounded_by_the_timeout_and_reports_NoServer()
    {
        // A login screen that blocks forever is a till that cannot be signed into during an
        // outage — the exact moment the shop most needs to keep selling.
        var handler = new RoutingHandler { PingDelay = TimeSpan.FromSeconds(30) };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://plutus.example") };
        var probe = new ConnectivityProbe(new PlutusApiClient(http)) { Timeout = TimeSpan.FromMilliseconds(150) };

        var started = DateTime.UtcNow;
        var status = await probe.CheckAsync();

        Assert.Equal(TillConnection.NoServer, status.State);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5), "the probe must not outlive its timeout");
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_a_connectivity_verdict()
    {
        // "The user navigated away" is not "the server is down", and recording it as one would
        // flap the indicator every time a screen closes mid-probe.
        var handler = new RoutingHandler { PingDelay = TimeSpan.FromSeconds(30) };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://plutus.example") };
        var probe = new ConnectivityProbe(new PlutusApiClient(http));

        using var cts = new CancellationTokenSource();
        var task = probe.CheckAsync(ct: cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
