using System;

namespace Plutus.Entities.Models
{
    // WP7.2 cash management (architecture §9.2): a cash session per till per business day is
    // the SEQUENCE OF EVENTS below, flowing through the same idempotent ingest discipline as
    // sales (client-minted event ids, replay-safe) so tills can queue them offline. Append-only.

    public enum CashEventType : byte
    {
        OpenFloat = 0,   // AmountPence = the float put in the drawer
        PaidIn = 1,      // AmountPence added to the drawer (reason required)
        PaidOut = 2,     // AmountPence removed from the drawer (reason required)
        XSnapshot = 3,   // non-closing count: CountedPence vs server-computed ExpectedPence
        ZClose = 4,      // closes the session; ONE per till per business day
    }

    public class CashEvent
    {
        public Guid Id { get; set; }               // client-minted UUIDv7 (idempotency anchor)
        public Guid TenantId { get; set; }
        public Guid TillId { get; set; }           // server-derived from the device (like sales)
        public Guid DeviceId { get; set; }
        public DateOnly BusinessDay { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public DateTime ReceivedAtUtc { get; set; }
        public CashEventType Type { get; set; }
        /// <summary>Float / paid-in / paid-out amount; 0 for X/Z.</summary>
        public long AmountPence { get; set; }
        /// <summary>The drawer count entered for X/Z.</summary>
        public long? CountedPence { get; set; }
        /// <summary>Server-computed at X/Z ingest: float + cash takings + ins − outs.</summary>
        public long? ExpectedPence { get; set; }
        /// <summary>Counted − expected, frozen into the Z record.</summary>
        public long? VariancePence { get; set; }
        public string? Reason { get; set; }
        public Guid? OperatorUserId { get; set; }
    }

    // WP7.1 payments (architecture §9.1, D13): the terminal/provider reports captures
    // independently of the sale POST. Captures that never find a matching sale tender are
    // the "orphaned payment" failure mode and surface in the unresolved queue.

    public enum PaymentEventStatus : byte { Captured = 0, Refunded = 1 }

    public class PaymentEvent
    {
        public Guid Id { get; set; }               // client/webhook-minted (idempotency anchor)
        public Guid TenantId { get; set; }
        public string Provider { get; set; }       // "fake" until the commercial adapter lands
        /// <summary>The terminal/provider reference — matched against SaleTenders.ProviderRef.</summary>
        public string ProviderRef { get; set; }
        public long AmountPence { get; set; }
        public PaymentEventStatus Status { get; set; }
        public DateTime CapturedAtUtc { get; set; }
        public DateTime ReceivedAtUtc { get; set; }
        public Guid? MatchedSaleId { get; set; }
        public DateTime? ResolvedAtUtc { get; set; }
    }
}
