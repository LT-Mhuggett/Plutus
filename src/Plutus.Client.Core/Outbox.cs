using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Plutus.Client.Core;

/// <summary>
/// WP3 status ladder for a locally-committed sale. This is the whole retry policy in one type:
/// what the server said decides whether the row is done, parked, or retried forever.
/// </summary>
public enum OutboxStatus
{
    /// <summary>Committed locally, not yet accepted. The only status the pusher drains.</summary>
    Pending = 0,
    /// <summary>201/200 — accepted (or already seen). Prunable once past the rolling window.</summary>
    Pushed = 1,
    /// <summary>202 — the server could not reconcile it and has quarantined it for review.
    /// <b>Never retried</b>: re-sending changes nothing and would hammer the endpoint.</summary>
    Quarantined = 2,
    /// <summary>400 — malformed or invariant-breaking. Skipped so it cannot block the queue, and
    /// surfaced in the UI as a count. <b>Never pruned</b> — a failed sale is money that needs a human.</summary>
    Failed = 3,
}

/// <summary>One queued sale: the exact contract payload plus its delivery state. PayloadJson is
/// stored verbatim so the pusher never re-serialises from entities — what was committed at
/// checkout is byte-for-byte what eventually reaches the server.</summary>
public sealed class OutboxEntry
{
    public Guid SaleId { get; set; }
    public long DeviceSeq { get; set; }
    public string PayloadJson { get; set; } = "";
    public OutboxStatus Status { get; set; }
    public DateTime? PushedAtUtc { get; set; }
    public string? ServerResponseJson { get; set; }
    /// <summary>Failed delivery attempts — drives the backoff, reset on success.</summary>
    public int Attempts { get; set; }
}

/// <summary>The till's local queue, abstracted so the engine is testable without SQLite (and so
/// WP2's schema can change underneath without touching the pusher).</summary>
public interface IOutboxStore
{
    /// <summary>Pending entries in DeviceSeq order — the order they were rung up in.</summary>
    Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(int max, CancellationToken ct = default);
    Task UpdateAsync(OutboxEntry entry, CancellationToken ct = default);
    Task<int> CountAsync(OutboxStatus status, CancellationToken ct = default);
    /// <summary>Age of the oldest Pending entry — reported on the heartbeat so the fleet
    /// dashboard can tell "quiet" from "quietly broken".</summary>
    Task<DateTime?> OldestPendingAtUtcAsync(CancellationToken ct = default);
}

/// <summary>
/// Exponential backoff for a till that cannot reach the server: 5s doubling to a 5-minute cap,
/// then retried forever. Deliberately NOT capped by attempt count — a shop that loses its line
/// for three days must still drain when it comes back, and a sale is never dropped for being old.
/// Pure and clock-free so the policy can be tested without waiting.
/// </summary>
public static class Backoff
{
    public static readonly TimeSpan Initial = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Max = TimeSpan.FromMinutes(5);

    public static TimeSpan For(int attempts)
    {
        if (attempts <= 0) return TimeSpan.Zero;
        // 5s, 10s, 20s, 40s … capped. Shift on a long to avoid overflowing at high attempt counts
        // (a till offline for a week reaches attempts in the thousands).
        var doublings = Math.Min(attempts - 1, 20);
        var ticks = Initial.Ticks * (1L << doublings);
        return ticks >= Max.Ticks || ticks <= 0 ? Max : TimeSpan.FromTicks(ticks);
    }
}

/// <summary>What the pusher decided for one entry — returned so callers (and tests) can assert
/// the policy without inspecting the store.</summary>
public sealed record PushOutcome(Guid SaleId, OutboxStatus Status, bool ShouldStop, string? Detail = null);
