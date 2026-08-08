using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP5b — the till noticeboard. These are the rules that decide what a human standing at a till is
/// shown, which is why they live in Client.Core and not in one app's viewmodel: two tills in the
/// same shop must not show different banners for the same event.
/// </summary>
public class NoticesClientTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public string? NotesJson = "[]";
        public string? AnnouncementsJson = "[]";
        public HttpStatusCode NotesStatus = HttpStatusCode.OK;
        public HttpStatusCode AnnouncementsStatus = HttpStatusCode.OK;
        public HttpStatusCode AckStatus = HttpStatusCode.OK;
        public Exception? Throws;
        public readonly List<string> Urls = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.PathAndQuery;
            Urls.Add(url);
            if (Throws != null) throw Throws;

            if (url.Contains("/ack")) return Task.FromResult(new HttpResponseMessage(AckStatus));
            if (url.Contains("/notifications"))
                return Task.FromResult(NotesStatus != HttpStatusCode.OK
                    ? new HttpResponseMessage(NotesStatus) : Json(NotesJson!));
            if (url.Contains("/announcements/active"))
                return Task.FromResult(AnnouncementsStatus != HttpStatusCode.OK
                    ? new HttpResponseMessage(AnnouncementsStatus) : Json(AnnouncementsJson!));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }

    private static (NoticesClient Client, Handler H) Build()
    {
        var h = new Handler();
        var api = new PlutusApiClient(new HttpClient(h) { BaseAddress = new Uri("https://till.example") });
        return (new NoticesClient(api), h);
    }

    private static string Note(Guid id, int? storeId, long order = 1, string? acked = null) =>
        $$"""
        {"id":"{{id}}","message":"Web order #{{order}} sold a shelf copy","wooOrderId":{{order}},
         "storeId":{{(storeId is int s ? s.ToString() : "null")}},"createdAtUtc":"2026-08-09T10:00:00Z",
         "ackedAtUtc":{{(acked is null ? "null" : $"\"{acked}\"")}}}
        """;

    private static string Ann(string severity) =>
        $$"""
        {"id":"{{Guid.NewGuid()}}","severity":"{{severity}}","title":"T","body":"B",
         "startsAtUtc":"2026-08-09T00:00:00Z","endsAtUtc":"2026-08-10T00:00:00Z"}
        """;

    // ── which announcements belong on a till ──

    [Theory]
    [InlineData("Maintenance", true)]
    [InlineData("Incident", true)]
    [InlineData("Info", false)]
    [InlineData("info", false)]      // case-insensitive: the wire is a string, not an enum
    [InlineData("  Info  ", false)]  // and whitespace must not smuggle Info onto a till
    public void Info_is_portal_only_and_everything_else_shows(string severity, bool shows) =>
        Assert.Equal(shows, NoticesClient.ShowsOnATill(severity));

    /// <summary>⚠ An unknown severity SHOWS. A backend that adds one will have added something at
    /// least as urgent as maintenance, and defaulting to "hide" would blank exactly the messages
    /// worth reading — silently, on every till, until someone shipped a new build.</summary>
    [Theory]
    [InlineData("Emergency")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unrecognised_severity_is_shown_rather_than_swallowed(string? severity) =>
        Assert.True(NoticesClient.ShowsOnATill(severity));

    // ── which pick notes belong on THIS till ──

    [Theory]
    [InlineData(7, 7, true)]        // addressed to this store
    [InlineData(7, 9, false)]       // addressed to another store — a walk to the wrong shelf
    [InlineData(null, 9, true)]     // unaddressed: everyone, rather than nobody
    public void A_note_addressed_to_a_store_reaches_only_that_stores_tills(
        int? noteStore, int tillStore, bool visible)
    {
        var note = new PickNoteDto(Guid.NewGuid(), "m", 1, noteStore, DateTime.UtcNow, null);
        Assert.Equal(visible, NoticesClient.IsForStore(note, tillStore));
    }

    /// <summary>A till that does not yet know its own store sees everything — it is newly enrolled
    /// or mid-placement-refresh. Showing a note that is not its problem costs a walk; hiding one
    /// that IS its problem costs an oversell.</summary>
    [Fact]
    public void A_till_that_does_not_know_its_store_sees_every_note()
    {
        var note = new PickNoteDto(Guid.NewGuid(), "m", 1, 42, DateTime.UtcNow, null);
        Assert.True(NoticesClient.IsForStore(note, null));
    }

    // ── the poll ──

    [Fact]
    public async Task Poll_filters_by_store_drops_acked_and_hides_Info()
    {
        var (client, h) = Build();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var acked = Guid.NewGuid();
        h.NotesJson = $"[{Note(mine, 7, 100)},{Note(theirs, 9, 200)},{Note(acked, 7, 300, "2026-08-09T11:00:00Z")}]";
        h.AnnouncementsJson = $"[{Ann("Info")},{Ann("Incident")}]";

        var outcome = await client.PollAsync(storeId: 7);

        Assert.True(outcome.Delivered);
        Assert.Equal(new[] { mine }, outcome.PickNotes.Select(n => n.Id).ToArray());
        Assert.Equal("Incident", Assert.Single(outcome.Announcements).Severity);
    }

    /// <summary>⚠ THE ONE THAT MATTERS. A failed poll must not read as "nothing to show", or the
    /// first flaky minute clears a live incident banner off every till in the estate and nobody
    /// finds out why the card terminals stopped working.</summary>
    [Fact]
    public async Task A_failed_poll_reports_failure_rather_than_an_empty_board()
    {
        var (client, h) = Build();
        h.NotesStatus = HttpStatusCode.InternalServerError;
        h.AnnouncementsStatus = HttpStatusCode.InternalServerError;

        var outcome = await client.PollAsync(storeId: 7);

        Assert.False(outcome.Delivered);
        Assert.Empty(outcome.PickNotes);
        Assert.Empty(outcome.Announcements);
    }

    /// <summary>An EMPTY board that was genuinely delivered is different from a failed one, and the
    /// caller has to be able to tell — this is the case that lets a banner be cleared correctly.</summary>
    [Fact]
    public async Task An_empty_board_that_actually_arrived_is_Delivered()
    {
        var (client, _) = Build();
        var outcome = await client.PollAsync(storeId: 7);
        Assert.True(outcome.Delivered);
        Assert.Empty(outcome.PickNotes);
    }

    /// <summary>Half a board is still a board: if announcements answer and pick notes do not, show
    /// what arrived rather than blanking both.</summary>
    [Fact]
    public async Task One_feed_failing_does_not_discard_the_other()
    {
        var (client, h) = Build();
        h.NotesStatus = HttpStatusCode.InternalServerError;
        h.AnnouncementsJson = $"[{Ann("Maintenance")}]";

        var outcome = await client.PollAsync(storeId: 7);

        Assert.True(outcome.Delivered);
        Assert.Empty(outcome.PickNotes);
        Assert.Single(outcome.Announcements);
    }

    [Fact]
    public async Task A_thrown_transport_error_never_escapes_the_poll()
    {
        var (client, h) = Build();
        h.Throws = new HttpRequestException("the shop's broadband died mid-poll");

        var outcome = await client.PollAsync(storeId: 7);

        Assert.False(outcome.Delivered);
    }

    // ── the ack ──

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NoContent, true)]
    // ⚠ 404 is SUCCESS: the note is already gone (acked by another till, or never this tenant's).
    // Retrying forever against a note that will never exist would park a permanent error on screen
    // that no human could clear.
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public async Task Ack_treats_an_already_gone_note_as_done_and_a_server_fault_as_retryable(
        HttpStatusCode status, bool acked)
    {
        var (client, h) = Build();
        h.AckStatus = status;
        Assert.Equal(acked, await client.AckAsync(Guid.NewGuid()));
    }

    /// <summary>⚠ An ack must not succeed offline. It is a claim that a human took stock off a
    /// shelf, and it has to reach the shop waiting to ship the order — so a failed ack leaves the
    /// note unacked and it reappears next poll. Irritating, and safe; the other direction silently
    /// oversells a web order.</summary>
    [Fact]
    public async Task Ack_fails_closed_when_the_network_is_gone()
    {
        var (client, h) = Build();
        h.Throws = new HttpRequestException("no route to host");
        Assert.False(await client.AckAsync(Guid.NewGuid()));
    }
}
