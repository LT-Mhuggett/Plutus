using System;
using System.Collections.Generic;
using System.Linq;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;

namespace Plutus.Client.Core;

/// <summary>
/// Turns the discount-audit RULE into wire bytes, and back.
///
/// ⚠ IT LIVES HERE FOR THE `SaleDtoTenders` REASON: `Plutus.Contracts.Client` references nothing at
/// all — it is the wire shape and only that — while `SharedKernel.DiscountAudit` owns what makes an
/// authority valid. Mapping between them is logic, and this project is the one that owns both.
///
/// ⚠ EVERY AUTHORITY ON THE WIRE CAME THROUGH HERE. Hand-rolling a `DiscountAuthorityRef` elsewhere
/// would skip <see cref="DiscountAudit.Authorise"/>, and the mandatory reason would then be
/// mandatory on whichever till remembered.
/// </summary>
public static class DiscountAuthorityWire
{
    /// <summary>
    /// One authority as the wire carries it.
    ///
    /// ⚠ Ids are written with "D" formatting — the same 8-4-4-4-12 shape `ReturnRef.OriginSaleId`
    /// uses — so anything reading this JSON parses ids one way, not two.
    /// </summary>
    public static DiscountAuthorityRef ToWire(this DiscountAuthority authority) => new()
    {
        Reason = authority.Reason,
        AmountPence = authority.AmountPence,
        RequestedBy = authority.RequestedByUserId == Guid.Empty
            ? null
            : authority.RequestedByUserId.ToString("D"),
        AuthorisedBy = authority.AuthorisedByUserId is Guid a && a != Guid.Empty
            ? a.ToString("D")
            : null,
        AuthorisedByName = authority.AuthorisedByName,
    };

    /// <summary>
    /// Every authority on a line, in wire form.
    ///
    /// ⚠⚠ EACH ONE'S `AmountPence` IS ALREADY THIS LINE'S SHARE, and nothing here re-derives it.
    /// `DiscountApportionment.Across` has already decided how a basket-wide discount splits; a second
    /// apportionment here would be the C2 drift failure inside a single process — the audit trail
    /// would say a line took 334p while the line itself says 333p, and the two would disagree only on
    /// baskets that do not divide evenly, which is exactly when nobody is checking.
    /// </summary>
    public static List<DiscountAuthorityRef>? ToWire(IReadOnlyList<DiscountAuthority>? authorities) =>
        authorities is null || authorities.Count == 0
            ? null
            : authorities.Select(ToWire).ToList();

    /// <summary>
    /// Read the authorities back off a line — for a receipt, a refund screen or an audit view.
    ///
    /// ⚠ Never throws on a line written by an older till. Absent means "this till did not record
    /// it", which is not the same as "nobody authorised it", and a caller that renders the two
    /// identically is asserting something it does not know.
    /// </summary>
    public static IReadOnlyList<DiscountAuthorityRef> AuthoritiesOn(string? discountsJson) =>
        LineMeta.FromJson(discountsJson)?.DiscountAuthority?.ToList()
        ?? (IReadOnlyList<DiscountAuthorityRef>)Array.Empty<DiscountAuthorityRef>();
}
