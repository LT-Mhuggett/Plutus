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
/// WP5 — the till's sync loop.
///
/// The rule every test here defends: <b>sync may never block selling.</b> A heartbeat that fails, a
/// catalogue that will not download, a server that has gone away — none of it is allowed to reach
/// the operator ringing up a customer. Sync keeps a till CURRENT; the outbox keeps it CORRECT, and
/// only the second one gets to have opinions about whether a sale can proceed.
/// </summary>
public class SyncClientTests
{
    // ── test doubles ──

    private sealed class FakeStore : ISyncStore
    {
        public string? Cursor;
        public int Depth;
        public TimeSpan? OldestAge;
        public readonly List<CatalogueItemDto> Applied = new();
        public int ApplyCalls;

        public Task<string?> GetCatalogueCursorAsync(CancellationToken ct = default) => Task.FromResult(Cursor);

        public Task ApplyCatalogueAsync(IReadOnlyList<CatalogueItemDto> items, string? cursor, CancellationToken ct = default)
        {
            ApplyCalls++;
            Applied.AddRange(items);
            Cursor = cursor;
            return Task.CompletedTask;
        }

        public Task<int> OutboxDepthAsync(CancellationToken ct = default) => Task.FromResult(Depth);
        public Task<TimeSpan?> OldestPendingAgeAsync(CancellationToken ct = default) => Task.FromResult(OldestAge);
    }

    /// <summary>Replies per path with a scripted queue, so a test can stage multi-page feeds.</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly Queue<string> CataloguePages = new();
        public string? HeartbeatJson;
        public HttpStatusCode HeartbeatStatus = HttpStatusCode.OK;
        public Exception? CatalogueThrows;
        public readonly List<string> Urls = new();
        public string? LastHeartbeatBody;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.PathAndQuery;
            Urls.Add(url);

            if (url.Contains("/heartbeat"))
            {
                LastHeartbeatBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
                if (HeartbeatStatus != HttpStatusCode.OK) return new HttpResponseMessage(HeartbeatStatus);
                return Json(HeartbeatJson ?? "{}");
            }

            if (url.Contains("/catalogue/changes"))
            {
                if (CatalogueThrows != null) throw CatalogueThrows;
                return CataloguePages.Count > 0
                    ? Json(CataloguePages.Dequeue())
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }

    private static (SyncClient Sync, FakeStore Store, ScriptedHandler Handler) Build()
    {
        var handler = new ScriptedHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://plutus.example") };
        var store = new FakeStore();
        return (new SyncClient(new PlutusApiClient(http), store), store, handler);
    }

    private static string Page(string? cursor, bool hasMore, params string[] idOnes)
    {
        var items = string.Join(",", idOnes.Select(id =>
            $"{{\"id\":\"{Guid.NewGuid()}\",\"idOne\":\"{id}\",\"name\":\"{id}\",\"pricePence\":100," +
            "\"exPricePence\":83,\"taxId\":1,\"categoryId\":null,\"stockUntracked\":false," +
            "\"removed\":false,\"updatedAtUtc\":\"2026-08-08T12:00:00Z\"}"));
        var c = cursor is null ? "null" : $"\"{cursor}\"";
        return $"{{\"cursor\":{c},\"hasMore\":{(hasMore ? "true" : "false")},\"items\":[{items}]}}";
    }

    // ── the catalogue feed ──

    [Fact]
    public async Task A_single_page_applies_and_stores_the_cursor()
    {
        var (sync, store, handler) = Build();
        handler.CataloguePages.Enqueue(Page("100:AAA", hasMore: false, "AAA"));

        var outcome = await sync.SyncCatalogueAsync();

        Assert.True(outcome.Succeeded);
        Assert.True(outcome.UpToDate);
        Assert.Equal(1, outcome.ItemsApplied);
        Assert.Equal("100:AAA", store.Cursor);
    }

    [Fact]
    public async Task It_follows_hasMore_until_the_feed_is_drained()
    {
        var (sync, store, handler) = Build();
        handler.CataloguePages.Enqueue(Page("100:AAA", hasMore: true, "AAA"));
        handler.CataloguePages.Enqueue(Page("200:BBB", hasMore: true, "BBB"));
        handler.CataloguePages.Enqueue(Page("300:CCC", hasMore: false, "CCC"));

        var outcome = await sync.SyncCatalogueAsync();

        Assert.Equal(3, outcome.Pages);
        Assert.Equal(3, outcome.ItemsApplied);
        Assert.True(outcome.UpToDate);
        Assert.Equal("300:CCC", store.Cursor);
    }

    [Fact]
    public async Task It_resumes_from_the_stored_cursor_rather_than_refetching_everything()
    {
        // A till that re-downloaded 20k items on every sync would be a till nobody leaves running.
        var (sync, store, handler) = Build();
        store.Cursor = "500:MMM";
        handler.CataloguePages.Enqueue(Page("600:NNN", hasMore: false, "NNN"));

        await sync.SyncCatalogueAsync();

        Assert.Contains(handler.Urls, u => u.Contains("since=500%3AMMM"));
    }

    [Fact]
    public async Task A_failure_midway_KEEPS_the_pages_already_applied()
    {
        // ⚠ Partial progress must survive. Discarding it would mean a till on a flaky line never
        // finishes a first sync at all — it would restart from zero every time.
        var (sync, store, handler) = Build();
        handler.CataloguePages.Enqueue(Page("100:AAA", hasMore: true, "AAA"));
        // Second page: the queue is empty, so the handler answers 500.

        var outcome = await sync.SyncCatalogueAsync();

        Assert.False(outcome.Succeeded);
        Assert.Equal(1, outcome.ItemsApplied);
        Assert.Equal("100:AAA", store.Cursor); // kept, so the next run resumes here
    }

    [Fact]
    public async Task A_dead_network_is_an_outcome_not_an_exception()
    {
        var (sync, _, handler) = Build();
        handler.CatalogueThrows = new HttpRequestException("No such host is known.");

        var outcome = await sync.SyncCatalogueAsync();

        Assert.False(outcome.Succeeded);
        Assert.Contains("No such host", outcome.Error);
    }

    [Fact]
    public async Task An_empty_page_does_not_touch_the_store()
    {
        // The common case: nothing changed. It must cost nothing — no transaction, no write.
        var (sync, store, handler) = Build();
        store.Cursor = "100:AAA";
        handler.CataloguePages.Enqueue(Page("100:AAA", hasMore: false));

        var outcome = await sync.SyncCatalogueAsync();

        Assert.True(outcome.UpToDate);
        Assert.Equal(0, store.ApplyCalls);
    }

    [Fact]
    public async Task Paging_is_bounded_so_one_run_cannot_spin_forever()
    {
        // A server that always says hasMore (a bug, or a cursor that will not advance) must not
        // pin a till in a loop. The cursor is saved, so the next run continues.
        var (sync, _, handler) = Build();
        for (var i = 0; i < SyncClient.MaxPagesPerRun + 10; i++)
            handler.CataloguePages.Enqueue(Page($"{i}:X", hasMore: true, $"X{i}"));

        var outcome = await sync.SyncCatalogueAsync();

        Assert.Equal(SyncClient.MaxPagesPerRun, outcome.Pages);
        Assert.False(outcome.UpToDate);
        Assert.True(outcome.Succeeded); // a bound, not a failure
    }


    // ── "is this till out of date?" (Matt, 2026-08-11) ──

    /// <summary>
    /// The beat now answers *"am I behind?"*, which it never used to — the heartbeat carried no
    /// version at all. ⚠ The comparison lives HERE, once, so every till answers it the same way.
    /// </summary>
    [Fact]
    public async Task A_till_behind_the_expected_build_is_told_which_build_to_get()
    {
        var (sync, _, handler) = Build();
        handler.HeartbeatJson =
            "{\"catalogueCursor\":null,\"syncNow\":false,\"locked\":false,\"lockReason\":null," +
            "\"serverUtcNow\":\"2026-08-11T12:00:00Z\",\"expectedMauiVersion\":\"1.46.0\"}";

        var outcome = await sync.BeatAsync(Guid.NewGuid(), "1.45.0");

        Assert.Equal("1.46.0", outcome.UpdateAvailable);
    }

    /// <summary>
    /// ⚠⚠ THE CASE A STRING COMPARE GETS WRONG, end to end. `"1.10.0" &lt; "1.9.0"` is TRUE to a
    /// string comparer, so a till on the TENTH release of a series would be told for ever that it
    /// was behind the ninth. This till is already on 1.46.0 — that range is not hypothetical.
    /// </summary>
    [Fact]
    public async Task A_till_AHEAD_of_the_expected_build_is_told_nothing()
    {
        var (sync, _, handler) = Build();
        handler.HeartbeatJson =
            "{\"catalogueCursor\":null,\"syncNow\":false,\"locked\":false,\"lockReason\":null," +
            "\"serverUtcNow\":\"2026-08-11T12:00:00Z\",\"expectedMauiVersion\":\"1.9.0\"}";

        Assert.Null((await sync.BeatAsync(Guid.NewGuid(), "1.10.0")).UpdateAvailable);
    }

    /// <summary>⚠ Up to date is SILENT — equal is not behind, or every correct till nags for ever.</summary>
    [Fact]
    public async Task A_current_till_is_told_nothing()
    {
        var (sync, _, handler) = Build();
        handler.HeartbeatJson =
            "{\"catalogueCursor\":null,\"syncNow\":false,\"locked\":false,\"lockReason\":null," +
            "\"serverUtcNow\":\"2026-08-11T12:00:00Z\",\"expectedMauiVersion\":\"1.46.0\"}";

        Assert.Null((await sync.BeatAsync(Guid.NewGuid(), "1.46.0")).UpdateAvailable);
    }

    /// <summary>
    /// ⚠ NO EXPECTED VERSION SET = SAY NOTHING, and this is the shipped default. The feature is
    /// inert until a platform admin deliberately turns it on, so a fresh install never greets its
    /// owner with an upgrade banner. It is also what every till sees on a backend that predates
    /// the field, since the JSON simply has no such property.
    /// </summary>
    [Fact]
    public async Task No_expected_version_means_no_prompt()
    {
        var (sync, _, handler) = Build();
        handler.HeartbeatJson =
            "{\"catalogueCursor\":null,\"syncNow\":false,\"locked\":false,\"lockReason\":null," +
            "\"serverUtcNow\":\"2026-08-11T12:00:00Z\"}";

        Assert.Null((await sync.BeatAsync(Guid.NewGuid(), "1.45.0")).UpdateAvailable);
    }

    /// <summary>
    /// ⚠⚠ A `0.0.0` TILL IS NEVER NAGGED. That is the sentinel for a build outside the release
    /// process — and, until it was fixed on 2026-08-11, what every Mac-built web bundle reported
    /// because the version file could not be found. Treating it as ancient would have told an estate
    /// of correctly-updated tills to upgrade to what they were already running.
    /// </summary>
    [Fact]
    public async Task A_build_outside_the_release_process_is_not_nagged()
    {
        var (sync, _, handler) = Build();
        handler.HeartbeatJson =
            "{\"catalogueCursor\":null,\"syncNow\":false,\"locked\":false,\"lockReason\":null," +
            "\"serverUtcNow\":\"2026-08-11T12:00:00Z\",\"expectedMauiVersion\":\"1.46.0\"}";

        Assert.Null((await sync.BeatAsync(Guid.NewGuid(), "0.0.0")).UpdateAvailable);
    }
    // ── the heartbeat ──

    [Fact]
    public async Task A_beat_reports_the_outbox_and_reads_back_the_signals()
    {
        var (sync, store, handler) = Build();
        store.Depth = 7;
        store.OldestAge = TimeSpan.FromMinutes(30);
        handler.HeartbeatJson =
            "{\"catalogueCursor\":\"900:ZZZ\",\"syncNow\":true,\"locked\":false,\"lockReason\":null," +
            "\"serverUtcNow\":\"2026-08-08T12:00:00Z\"}";

        var outcome = await sync.BeatAsync(Guid.NewGuid(), "1.0.0");

        Assert.True(outcome.Delivered);
        Assert.True(outcome.SyncNow);
        Assert.Contains("\"outboxDepth\":7", handler.LastHeartbeatBody);
        Assert.Contains("\"oldestUnsyncedAgeSeconds\":1800", handler.LastHeartbeatBody);
    }

    [Fact]
    public async Task A_different_server_cursor_means_the_catalogue_needs_pulling()
    {
        var (sync, store, handler) = Build();
        store.Cursor = "100:AAA";
        handler.HeartbeatJson =
            "{\"catalogueCursor\":\"900:ZZZ\",\"syncNow\":false,\"locked\":false,\"lockReason\":null," +
            "\"serverUtcNow\":\"2026-08-08T12:00:00Z\"}";

        Assert.True((await sync.BeatAsync(Guid.NewGuid(), null)).CatalogueStale);
    }

    [Fact]
    public async Task The_same_cursor_costs_nothing_further()
    {
        // Comparing cursors rather than trusting a flag keeps the overwhelmingly common case — a
        // catalogue nobody edited this minute — down to one call.
        var (sync, store, handler) = Build();
        store.Cursor = "900:ZZZ";
        handler.HeartbeatJson =
            "{\"catalogueCursor\":\"900:ZZZ\",\"syncNow\":false,\"locked\":false,\"lockReason\":null," +
            "\"serverUtcNow\":\"2026-08-08T12:00:00Z\"}";

        Assert.False((await sync.BeatAsync(Guid.NewGuid(), null)).CatalogueStale);
    }

    [Fact]
    public async Task A_lock_signal_is_carried_with_its_reason()
    {
        var (sync, _, handler) = Build();
        handler.HeartbeatJson =
            "{\"catalogueCursor\":null,\"syncNow\":false,\"locked\":true,\"lockReason\":\"Returned to head office\"," +
            "\"serverUtcNow\":\"2026-08-08T12:00:00Z\"}";

        var outcome = await sync.BeatAsync(Guid.NewGuid(), null);

        Assert.True(outcome.Locked);
        Assert.Equal("Returned to head office", outcome.LockReason);
    }

    [Fact]
    public async Task A_failing_heartbeat_is_SILENT_and_never_locks_the_till()
    {
        // ⚠ THE REGRESSION THIS EXISTS TO PREVENT. If a 500 or a dead network were ever read as
        // "locked", every till in the estate would stop selling the moment the backend hiccupped —
        // a self-inflicted outage far worse than not knowing where the tills are.
        var (sync, _, handler) = Build();
        handler.HeartbeatStatus = HttpStatusCode.InternalServerError;

        var outcome = await sync.BeatAsync(Guid.NewGuid(), null);

        Assert.False(outcome.Delivered);
        Assert.False(outcome.Locked);
        Assert.False(outcome.SyncNow);
        Assert.False(outcome.CatalogueStale);
    }

    [Fact]
    public async Task A_beat_with_an_empty_outbox_sends_a_null_age_not_a_zero()
    {
        // Zero would read as "there is a sale, and it is brand new". Null says "there is nothing",
        // and the fleet list shows a different thing for each.
        var (sync, _, handler) = Build();
        handler.HeartbeatJson = "{\"catalogueCursor\":null,\"syncNow\":false,\"locked\":false," +
            "\"lockReason\":null,\"serverUtcNow\":\"2026-08-08T12:00:00Z\"}";

        await sync.BeatAsync(Guid.NewGuid(), null);

        Assert.Contains("\"oldestUnsyncedAgeSeconds\":null", handler.LastHeartbeatBody);
    }
}
