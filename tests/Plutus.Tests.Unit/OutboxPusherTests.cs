using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP1/WP3: the till outbox retry contract. These pin the rules that decide whether a shop's
/// takings reach the server — every one of them is a way a real till has broken in other POS
/// systems: a poison sale blocking the queue, a quarantined sale retried forever, a token
/// expiring mid-drain, an offline shop giving up on old sales.
/// </summary>
public class OutboxPusherTests
{
    // ── test doubles ──

    private sealed class FakeStore : IOutboxStore
    {
        public readonly List<OutboxEntry> Entries = new();
        public Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(int max, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OutboxEntry>>(
                Entries.Where(e => e.Status == OutboxStatus.Pending).OrderBy(e => e.DeviceSeq).Take(max).ToList());
        public Task UpdateAsync(OutboxEntry entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> CountAsync(OutboxStatus status, CancellationToken ct = default) =>
            Task.FromResult(Entries.Count(e => e.Status == status));
        public Task<DateTime?> OldestPendingAtUtcAsync(CancellationToken ct = default) => Task.FromResult<DateTime?>(null);
    }

    /// <summary>Replies with a scripted status per call, and records what it was sent.</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<object> _script;
        public readonly List<string> Bodies = new();
        public readonly List<string?> AuthHeaders = new();
        public ScriptedHandler(params object[] script) => _script = new Queue<object>(script);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            AuthHeaders.Add(request.Headers.Authorization?.Parameter);
            var next = _script.Count > 0 ? _script.Dequeue() : HttpStatusCode.Created;
            if (next is Exception ex) throw ex;
            var status = (HttpStatusCode)next;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent("{\"status\":\"recorded\"}", System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    private static OutboxEntry Entry(long seq) => new()
    {
        SaleId = Uuid7.New(), DeviceSeq = seq, PayloadJson = $"{{\"deviceSeq\":{seq}}}", Status = OutboxStatus.Pending,
    };

    private static (OutboxPusher Pusher, FakeStore Store, ScriptedHandler Handler) Build(params object[] script)
    {
        var store = new FakeStore();
        var handler = new ScriptedHandler(script);
        var api = new PlutusApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        return (new OutboxPusher(store, api), store, handler);
    }

    // ── the policy ──

    [Fact]
    public async Task Drains_in_deviceSeq_order_and_marks_them_pushed()
    {
        var (pusher, store, handler) = Build(HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.Created);
        // deliberately inserted out of order — the queue must not depend on insertion order
        store.Entries.AddRange(new[] { Entry(3), Entry(1), Entry(2) });

        var outcomes = await pusher.DrainAsync();

        Assert.Equal(3, outcomes.Count);
        Assert.All(outcomes, o => Assert.Equal(OutboxStatus.Pushed, o.Status));
        Assert.Equal(new[] { "{\"deviceSeq\":1}", "{\"deviceSeq\":2}", "{\"deviceSeq\":3}" }, handler.Bodies);
    }

    [Fact]
    public async Task A_poison_sale_is_failed_and_does_NOT_block_the_ones_behind_it()
    {
        // The single most important rule here: one malformed sale must never strand a shop's queue.
        var (pusher, store, _) = Build(HttpStatusCode.BadRequest, HttpStatusCode.Created, HttpStatusCode.Created);
        store.Entries.AddRange(new[] { Entry(1), Entry(2), Entry(3) });

        var outcomes = await pusher.DrainAsync();

        Assert.Equal(3, outcomes.Count);
        Assert.Equal(OutboxStatus.Failed, outcomes[0].Status);
        Assert.Equal(OutboxStatus.Pushed, outcomes[1].Status);
        Assert.Equal(OutboxStatus.Pushed, outcomes[2].Status);
    }

    [Fact]
    public async Task Quarantine_is_terminal_and_a_duplicate_200_counts_as_delivered()
    {
        var (pusher, store, _) = Build(HttpStatusCode.Accepted, HttpStatusCode.OK);
        store.Entries.AddRange(new[] { Entry(1), Entry(2) });

        var outcomes = await pusher.DrainAsync();

        Assert.Equal(OutboxStatus.Quarantined, outcomes[0].Status);   // never retried
        Assert.Equal(OutboxStatus.Pushed, outcomes[1].Status);        // server already had it
        Assert.All(outcomes, o => Assert.False(o.ShouldStop));
    }

    [Fact]
    public async Task A_network_failure_keeps_the_sale_pending_and_stops_the_drain()
    {
        var (pusher, store, handler) = Build(new HttpRequestException("no route to host"));
        store.Entries.AddRange(new[] { Entry(1), Entry(2), Entry(3) });

        var outcomes = await pusher.DrainAsync();

        Assert.Single(outcomes);                                   // stopped, didn't burn the queue
        Assert.Equal(OutboxStatus.Pending, outcomes[0].Status);    // still owed
        Assert.True(outcomes[0].ShouldStop);
        Assert.Equal(1, store.Entries.Single(e => e.DeviceSeq == 1).Attempts);
        Assert.Single(handler.Bodies);
    }

    [Fact]
    public async Task A_401_mid_drain_re_mints_the_token_once_and_carries_on()
    {
        var store = new FakeStore();
        var handler = new ScriptedHandler(HttpStatusCode.Unauthorized, HttpStatusCode.Created);
        var tokens = new StubTokens();
        var api = new PlutusApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") }, tokens);
        var pusher = new OutboxPusher(store, api, tokens);
        store.Entries.Add(Entry(1));

        var outcomes = await pusher.DrainAsync();

        Assert.Equal(OutboxStatus.Pushed, outcomes[0].Status);
        Assert.Equal(1, tokens.Invalidations);              // exactly once — not a retry storm
        Assert.Equal(2, handler.Bodies.Count);
    }

    private sealed class StubTokens : IDeviceTokenProvider
    {
        public int Invalidations;
        public Task<string?> GetAccessTokenAsync(CancellationToken ct = default) => Task.FromResult<string?>("tok");
        public void Invalidate() => Invalidations++;
    }

    // ── backoff ──

    [Fact]
    public void Backoff_doubles_from_5s_caps_at_5min_and_never_overflows()
    {
        Assert.Equal(TimeSpan.Zero, Backoff.For(0));
        Assert.Equal(TimeSpan.FromSeconds(5), Backoff.For(1));
        Assert.Equal(TimeSpan.FromSeconds(10), Backoff.For(2));
        Assert.Equal(TimeSpan.FromSeconds(40), Backoff.For(4));
        Assert.Equal(Backoff.Max, Backoff.For(10));
        // a till offline for days reaches very high attempt counts — must stay at the cap, not
        // wrap negative and start hammering
        Assert.Equal(Backoff.Max, Backoff.For(5_000));
        Assert.Equal(Backoff.Max, Backoff.For(int.MaxValue));
    }
}
