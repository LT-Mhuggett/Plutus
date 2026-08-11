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

        /// <summary>
        /// Reverses a <see cref="ZClose"/> so the day can trade again — supervisor and above
        /// (Matt, 2026-08-11: *"A supervisor or above needs to be able to reverse the close."*).
        /// ⚠⚠ COMPENSATING, NOT A DELETION: the close stays with its counted figure and variance,
        /// and this goes on top. ⚠ Carries no money. ⚠ **Value 5 is permanent** — stored as a byte,
        /// so renumbering re-labels history.
        /// </summary>
        ZReopen = 5,
    }

    /// <summary>
    /// Is a business day closed to further trade?
    ///
    /// ⚠⚠ IT LIVES BESIDE THE ENTITY BECAUSE THREE PLACES ASK IT AND THEY MUST NOT DISAGREE: the
    /// cash guard (`Plutus.Cash`), the sales gate (`Plutus.Sales`) and the till's own local copy.
    /// `Plutus.Sales` cannot reference `Plutus.Cash`, so putting the rule in either would have forced
    /// a second implementation — and a day that is open for a float and shut for a sale is a
    /// disagreement nobody finds until the figures stop matching.
    ///
    /// ⚠ THE LATEST Z-MARK WINS — never "does a close exist". `Any(ZClose)` refuses a reopened day
    /// for ever, which is the trap the reopen exists to escape.
    ///
    /// ⚠ TIES GO TO CLOSED. Two events on one timestamp is a clock artefact, and the safe reading of
    /// an ambiguous drawer is that it is shut: a wrongly-open day quietly adds sales to takings
    /// already counted and banked, while a wrongly-shut one asks somebody to press reopen again.
    /// </summary>
    public static class CashDay
    {
        public static bool IsClosed(IEnumerable<CashEvent> dayEvents)
        {
            DateTime? lastClose = null, lastReopen = null;

            foreach (var e in dayEvents)
            {
                if (e.Type == CashEventType.ZClose && (lastClose is null || e.OccurredAtUtc > lastClose))
                    lastClose = e.OccurredAtUtc;
                else if (e.Type == CashEventType.ZReopen && (lastReopen is null || e.OccurredAtUtc > lastReopen))
                    lastReopen = e.OccurredAtUtc;
            }

            if (lastClose is null) return false;
            return lastReopen is null || lastReopen.Value <= lastClose.Value;
        }
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
