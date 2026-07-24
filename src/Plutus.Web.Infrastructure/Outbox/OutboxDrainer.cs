using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Infrastructure.Outbox
{
    /// <summary>Turns a stored outbox row (EventType + JSON) back into its DomainEvent.</summary>
    public interface IOutboxEventCodec
    {
        DomainEvent Decode(string eventType, string payloadJson);
    }

    public sealed class DefaultOutboxEventCodec : IOutboxEventCodec
    {
        public DomainEvent Decode(string eventType, string payloadJson)
        {
            switch (eventType)
            {
                case nameof(SaleRecorded):
                    return JsonSerializer.Deserialize<SaleRecorded>(payloadJson);
                default:
                    throw new NotSupportedException("Unknown outbox event type: " + eventType);
            }
        }
    }

    public sealed class OutboxDispatcherOptions
    {
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);
        public int BatchSize { get; set; } = 100;
        /// <summary>Delays between retries; after these are exhausted the event is parked.
        /// Default 1s/5s/25s (= 3 retries). Tests set an empty array for no delay.</summary>
        public TimeSpan[] RetryBackoffs { get; set; } =
            { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(25) };
    }

    public struct DrainStats
    {
        public int Handled;
        public int Parked;
        public int Skipped; // already processed (dedupe)
    }

    /// <summary>
    /// Core outbox drain logic (T1.5), independent of the hosted-service timer so it is unit
    /// testable. For one consumer: read its offset, fetch the next ordered batch, and for each
    /// event — dedupe against ProcessedEvents, handle with bounded retry (park on exhaustion),
    /// and advance the offset in the SAME SaveChanges as the processed/dead-letter record. That
    /// atomicity is what makes a restart neither lose nor double-apply an event.
    /// </summary>
    public sealed class OutboxDrainer
    {
        private readonly IOutboxEventCodec _codec;
        private readonly OutboxDispatcherOptions _opts;
        private readonly ILogger _logger;

        public OutboxDrainer(IOutboxEventCodec codec, OutboxDispatcherOptions opts, ILogger logger = null)
        {
            _codec = codec;
            _opts = opts ?? new OutboxDispatcherOptions();
            _logger = logger;
        }

        public async Task<DrainStats> DrainConsumerAsync(MySqlDbContext db, IEventConsumer consumer, CancellationToken ct)
        {
            db.CurrentUser = "outbox-dispatcher";
            var name = consumer.Name;
            var stats = new DrainStats();

            var offsetRow = await db.ConsumerOffsets.FirstOrDefaultAsync(o => o.ConsumerName == name, ct);
            long offset = offsetRow?.LastOutboxId ?? 0;

            var batch = await db.OutboxEvents
                .Where(e => e.Id > offset).OrderBy(e => e.Id).Take(_opts.BatchSize).ToListAsync(ct);

            foreach (var evt in batch)
            {
                ct.ThrowIfCancellationRequested();

                bool already = await db.ProcessedEvents
                    .AnyAsync(p => p.ConsumerName == name && p.EventId == evt.EventId, ct);

                if (already)
                {
                    stats.Skipped++;
                }
                else
                {
                    var error = await TryHandleAsync(consumer, evt, ct);
                    if (error == null)
                    {
                        db.ProcessedEvents.Add(new ProcessedEvent
                        {
                            ConsumerName = name, EventId = evt.EventId, ProcessedAtUtc = DateTime.UtcNow,
                        });
                        stats.Handled++;
                    }
                    else
                    {
                        db.ConsumerDeadLetters.Add(new ConsumerDeadLetter
                        {
                            Id = Uuid7.New(), ConsumerName = name, EventId = evt.EventId, OutboxId = evt.Id,
                            EventType = evt.EventType, PayloadJson = evt.PayloadJson,
                            Error = Trunc(error, 2000), CreatedAtUtc = DateTime.UtcNow,
                        });
                        stats.Parked++;
                        _logger?.LogWarning("Outbox consumer {Consumer} parked event {EventId} ({Type}): {Error}",
                            name, evt.EventId, evt.EventType, error);
                    }
                }

                // Advance the offset atomically with the processed / dead-letter write.
                if (offsetRow == null)
                {
                    offsetRow = new ConsumerOffset { ConsumerName = name, LastOutboxId = evt.Id, UpdatedAtUtc = DateTime.UtcNow };
                    db.ConsumerOffsets.Add(offsetRow);
                }
                else
                {
                    offsetRow.LastOutboxId = evt.Id;
                    offsetRow.UpdatedAtUtc = DateTime.UtcNow;
                }
                await db.SaveChangesAsync(ct);
            }

            return stats;
        }

        /// <summary>Max(OutboxEvents.Id) − this consumer's offset: how far behind it is.</summary>
        public static async Task<long> LagAsync(MySqlDbContext db, string consumerName, CancellationToken ct = default)
        {
            long maxId = await db.OutboxEvents.AnyAsync(ct) ? await db.OutboxEvents.MaxAsync(e => e.Id, ct) : 0;
            var off = await db.ConsumerOffsets.FirstOrDefaultAsync(o => o.ConsumerName == consumerName, ct);
            return maxId - (off?.LastOutboxId ?? 0);
        }

        private async Task<string> TryHandleAsync(IEventConsumer consumer, OutboxEvent evt, CancellationToken ct)
        {
            var backoffs = _opts.RetryBackoffs ?? Array.Empty<TimeSpan>();
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    var domainEvent = _codec.Decode(evt.EventType, evt.PayloadJson);
                    await consumer.HandleAsync(domainEvent, ct);
                    return null; // success
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    if (attempt >= backoffs.Length) return ex.Message; // retries exhausted → park
                    try { await Task.Delay(backoffs[attempt], ct); }
                    catch (OperationCanceledException) { throw; }
                }
            }
        }

        private static string Trunc(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max);
    }
}
