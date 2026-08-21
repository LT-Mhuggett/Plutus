using System;

namespace Plutus.Contracts.Client;

// ─────────────────────────────────────────────────────────────────────────────
// Scheduled discounts — "Wednesday Warhammer" (Discount plan, 2026-08-20). The portal sets these;
// every till reads them on its sync cadence and applies them itself.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One scheduled discount rule on the wire.
///
/// ⚠⚠ THE SCHEDULE IS SHIPPED RAW, NOT PRE-EVALUATED — the same design as
/// <see cref="OperatorGrantDto"/> and for the same reason. If the server answered "is this rule live
/// now" at sync time, a till that syncs on Monday and trades offline until Thursday would apply
/// Monday's answer all week: no Wednesday discount on Wednesday, or a Wednesday discount every day.
/// The till evaluates against its own clock at the moment of the sale, using
/// <c>Plutus.SharedKernel.ScheduledDiscounts</c> — the same code the portal previews with.
///
/// ⚠ <see cref="Id"/> is the catalogue discount's REAL id, and that is load-bearing: it is what lets
/// an auto-applied rule ride <c>LineMeta.discounts[]</c> and project into <c>Transaction_Discount</c>,
/// which is keyed on a real <c>DiscountId</c>. The members' discount uses a sentinel id and is kept
/// OFF that array precisely because a synthetic id would FK-fail the whole sale.
/// </summary>
/// <param name="Type">0 = a fixed amount off each unit, carried in <paramref name="FixedAmountPence"/>;
/// 1 = a percentage, carried in <paramref name="PercentFraction"/>.</param>
/// <param name="PercentFraction">A FRACTION — 0.10 is 10%. A ratio, not money.</param>
/// <param name="FixedAmountPence">⚠ INTEGER PENCE, per unit. The legacy `Discounts.Amount` column is
/// decimal POUNDS; the server converts once, on the way out, so no client ever has to know that.</param>
/// <param name="AutoApply">True = the till applies it by itself. False = a catalogue entry an
/// operator picks by hand, which is the whole difference between a rule and a list item.</param>
/// <param name="DaysOfWeekMask">Bit 0 = Sunday … bit 6 = Saturday; null = any day.</param>
/// <param name="WindowStartLocal">⚠ LOCAL wall-clock, while the validity bounds are UTC INSTANTS.</param>
public sealed record DiscountRuleDto(
    int Id,
    string Name,
    int Type,
    decimal PercentFraction,
    long FixedAmountPence,
    bool AutoApply,
    bool AllApplicable,
    byte? DaysOfWeekMask,
    TimeOnly? WindowStartLocal,
    TimeOnly? WindowEndLocal,
    DateTime? ValidFromUtc,
    DateTime? ValidToUtc,
    Guid[] CategoryIds,
    string[] ItemIdOnes);

/// <summary>
/// GET /api/v1/discounts/rules — the tenant's live rules.
///
/// ⚠ <see cref="AsOfUtc"/> is the SERVER's clock, as on the roster: it says when this answer was
/// true, so a till can say how stale its rules are without trusting its own clock to measure it.
/// </summary>
public sealed record DiscountRulesResult(DateTime AsOfUtc, DiscountRuleDto[] Rules);
