using System;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// Dedupe ledger for at-least-once delivery (T1.5): one row per (consumer, event) that a
    /// consumer has successfully handled. The dispatcher records this in the same transaction
    /// as advancing the consumer's offset, giving effectively-once semantics across restarts.
    /// Composite key (ConsumerName, EventId). Global/infra — not tenant-scoped.
    /// </summary>
    public class ProcessedEvent
    {
        public string ConsumerName { get; set; }
        public Guid EventId { get; set; }
        public DateTime ProcessedAtUtc { get; set; }
    }

    /// <summary>
    /// A parked event (T1.5): written after a consumer exhausts its retries, so a poison event
    /// never halts the others' progress. Surfaced in logs and GET /api/v1/ops/deadletters.
    /// </summary>
    public class ConsumerDeadLetter
    {
        public Guid Id { get; set; }
        public string ConsumerName { get; set; }
        public Guid EventId { get; set; }
        public long OutboxId { get; set; }
        public string EventType { get; set; }
        public string PayloadJson { get; set; }
        public string Error { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
