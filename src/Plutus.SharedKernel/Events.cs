namespace Plutus.SharedKernel;

/// <summary>Base for domain events carried through the transactional outbox (D8).</summary>
public abstract record DomainEvent(Guid EventId, Guid TenantId, DateTime OccurredAtUtc);

/// <summary>Published after a sale is durably written (write-then-publish, §2). Consumers
/// (stock, reporting, webstore sync) are downstream and may lag without losing a sale.</summary>
public sealed record SaleRecorded(
    Guid EventId,
    Guid TenantId,
    DateTime OccurredAtUtc,
    Guid SaleId,
    Guid DeviceId,
    long DeviceSeq,
    DateOnly BusinessDay) : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>Publish side of the in-process event bus; the MySQL-outbox implementation
/// lands in T1.5, a broker later — consumers depend only on "events arrive".</summary>
public interface IEventBus
{
    Task PublishAsync(DomainEvent e, CancellationToken ct);
}

/// <summary>A registered consumer of domain events. Must be idempotent (at-least-once
/// delivery + consumer-side dedupe = effectively-once).</summary>
public interface IEventConsumer
{
    string Name { get; }
    Task HandleAsync(DomainEvent e, CancellationToken ct);
}
