using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.Infrastructure.Outbox;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>WP1.5 outbox dispatch: consumers converge independently, a restart mid-batch neither
/// loses nor double-applies (dedupe), and a poison event parks after retries while later events
/// keep flowing.</summary>
public class OutboxDispatcherTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly OutboxDispatcherOptions NoDelay =
        new() { RetryBackoffs = Array.Empty<TimeSpan>() }; // park immediately on failure

    private sealed class RecordingConsumer : IEventConsumer
    {
        public RecordingConsumer(string name) => Name = name;
        public string Name { get; }
        public HashSet<Guid> Poison { get; } = new();
        public List<Guid> Handled { get; } = new();
        public Task HandleAsync(DomainEvent e, CancellationToken ct)
        {
            if (Poison.Contains(e.EventId)) throw new InvalidOperationException("poison");
            Handled.Add(e.EventId);
            return Task.CompletedTask;
        }
    }

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(Tenant)) { CurrentUser = "outbox-test" };

    // Seeds N SaleRecorded outbox rows; returns their EventIds in insertion order.
    private static List<Guid> Seed(SqliteConnection conn, int n)
    {
        using var ctx = Ctx(conn);
        var ids = new List<Guid>();
        for (var i = 0; i < n; i++)
        {
            var eid = Guid.NewGuid();
            var sr = new SaleRecorded(eid, Tenant, DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), i + 1, new DateOnly(2026, 7, 24));
            ctx.OutboxEvents.Add(new OutboxEvent
            {
                EventId = eid, TenantId = Tenant, EventType = nameof(SaleRecorded),
                PayloadJson = JsonSerializer.Serialize(sr), CreatedAtUtc = DateTime.UtcNow,
            });
            ids.Add(eid);
        }
        ctx.SaveChanges();
        return ids;
    }

    private static OutboxDrainer Drainer() => new(new DefaultOutboxEventCodec(), NoDelay);

    [Fact]
    public async Task Two_consumers_each_process_all_events_independently()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using (var c = Ctx(conn)) c.Database.EnsureCreated();
        var ids = Seed(conn, 3);

        var a = new RecordingConsumer("A");
        var b = new RecordingConsumer("B");
        using (var db = Ctx(conn)) await Drainer().DrainConsumerAsync(db, a, default);
        using (var db = Ctx(conn)) await Drainer().DrainConsumerAsync(db, b, default);

        Assert.Equal(ids, a.Handled);
        Assert.Equal(ids, b.Handled);
        using var ctx = Ctx(conn);
        Assert.Equal(3, await ctx.ProcessedEvents.CountAsync(p => p.ConsumerName == "A"));
        Assert.Equal(3, await ctx.ProcessedEvents.CountAsync(p => p.ConsumerName == "B"));
    }

    [Fact]
    public async Task Replaying_after_offset_reset_does_not_double_apply()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using (var c = Ctx(conn)) c.Database.EnsureCreated();
        Seed(conn, 3);

        var consumer = new RecordingConsumer("A");
        using (var db = Ctx(conn)) await Drainer().DrainConsumerAsync(db, consumer, default);
        Assert.Equal(3, consumer.Handled.Count);

        // Simulate a crash-restart that lost the offset advance: reset it and drain again.
        using (var db = Ctx(conn))
        {
            var off = await db.ConsumerOffsets.FirstAsync(o => o.ConsumerName == "A");
            off.LastOutboxId = 0;
            db.CurrentUser = "test";
            await db.SaveChangesAsync();
        }
        using (var db = Ctx(conn))
        {
            var stats = await Drainer().DrainConsumerAsync(db, consumer, default);
            Assert.Equal(3, stats.Skipped);   // dedupe caught all three
            Assert.Equal(0, stats.Handled);
        }
        Assert.Equal(3, consumer.Handled.Count); // still 3 — never re-invoked
    }

    [Fact]
    public async Task Poison_event_parks_after_retries_and_later_events_flow()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using (var c = Ctx(conn)) c.Database.EnsureCreated();
        var ids = Seed(conn, 3);

        var consumer = new RecordingConsumer("A");
        consumer.Poison.Add(ids[1]); // middle event always fails

        DrainStats stats;
        using (var db = Ctx(conn)) stats = await Drainer().DrainConsumerAsync(db, consumer, default);

        Assert.Equal(2, stats.Handled);
        Assert.Equal(1, stats.Parked);
        Assert.Equal(new[] { ids[0], ids[2] }, consumer.Handled); // 1st and 3rd flowed past the poison
        using var ctx = Ctx(conn);
        Assert.Equal(1, await ctx.ConsumerDeadLetters.CountAsync(d => d.ConsumerName == "A" && d.EventId == ids[1]));
        Assert.Equal(3, (await ctx.ConsumerOffsets.FirstAsync(o => o.ConsumerName == "A")).LastOutboxId); // advanced past all
    }

    [Fact]
    public async Task Lag_reports_distance_behind_head()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using (var c = Ctx(conn)) c.Database.EnsureCreated();
        Seed(conn, 5);

        using var ctx = Ctx(conn);
        Assert.Equal(5, await OutboxDrainer.LagAsync(ctx, "A")); // no offset yet → 5 behind
    }
}
