using System;
using System.Collections.Generic;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;

namespace Plutus.Client.Core;

/// <summary>
/// How a sale the PLATFORM holds was paid, in the form the refund rules want.
///
/// ⚠⚠ WHY THIS EXISTS — finding Y piece 4b, 2026-08-13. A till refunding a sale rung up on ANOTHER
/// till could not tell £2.00 cash + £2.40 card from £4.40 on a card, so it offered the whole refund
/// wherever the operator tapped. The server had been sending the tenders all along —
/// `GET /api/v1/sales/{saleId}` has projected them since it was written — and `SaleDto` simply had no
/// property for them. **The data was there; nobody had asked for it.** That is the same shape as the
/// seven built-and-uncalled components, one layer out.
///
/// ⚠ IT LIVES HERE, NOT ON THE DTO, because `Plutus.Contracts.Client` references nothing at all: it
/// is the wire shape and only that. Mapping a name to a rule's input is logic, and logic belongs in
/// this project, which already owns SharedKernel.
/// </summary>
public static class SaleDtoTenders
{
    /// <summary>
    /// What each tender took, as (wire byte, positive pence) — feed straight to
    /// <see cref="RefundRules.RefundCapacities"/>.
    ///
    /// ⚠ A tender whose name this build does not recognise is DROPPED, never guessed at. It then has
    /// no refundable capacity, so a refund to it is refused — which is the right way to fail. The
    /// lenient <see cref="Tenders.FromMethodName"/> would call it a CARD, which on this path means
    /// handing a card somebody else's money to give back.
    ///
    /// ⚠ MAGNITUDES. A refund's tenders are negative on the wire, and the rules compare positives.
    /// </summary>
    public static IEnumerable<KeyValuePair<byte, long>> TenderPairs(this SaleDto? sale)
    {
        if (sale?.Tenders is null) yield break;

        foreach (var t in sale.Tenders)
            if (Tenders.TryFromWireName(t.TenderType, out var type) && t.AmountPence != 0)
                yield return new KeyValuePair<byte, long>(type, Math.Abs(t.AmountPence));
    }
}
