using System;
using System.Collections.Generic;
using System.Linq;

namespace Plutus.SharedKernel;

/// <summary>
/// WP3.1: the fixed, CODE-DEFINED permission catalogue (architecture §7.2) — portal and POS
/// permissions in ONE catalogue so there is one admin surface. Roles bundle these; grants may
/// carry a pence ceiling where the permission is amount-limited (e.g. pos.refund with
/// MaxPence 2000 renders as "pos.refund.max:2000"). The catalogue is not stored in the DB —
/// grants store the codes, and unknown codes are rejected at write time.
/// </summary>
public static class PermissionCatalogue
{
    // ── portal ──
    public const string PortalFinancialsView = "portal.financials.view";
    public const string PortalUsersManage = "portal.users.manage";
    public const string PortalStockAdjust = "portal.stock.adjust";
    public const string PortalPricesManage = "portal.prices.manage";
    public const string PortalTillsEnrol = "portal.tills.enrol";
    public const string PortalReportsView = "portal.reports.view";
    /// <summary>WP3.2: company + store administration (names, addresses, opening hours).</summary>
    public const string PortalCompanyManage = "portal.company.manage";

    // ── POS ──
    public const string PosSell = "pos.sell";
    public const string PosRefund = "pos.refund";           // ceiling-capable
    public const string PosVoid = "pos.void";
    public const string PosDiscount = "pos.discount";       // ceiling-capable
    public const string PosPriceOverride = "pos.price-override";
    public const string PosNoSale = "pos.no-sale";
    public const string PosReportsView = "pos.reports.view";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        PortalFinancialsView, PortalUsersManage, PortalStockAdjust, PortalPricesManage,
        PortalTillsEnrol, PortalReportsView, PortalCompanyManage,
        PosSell, PosRefund, PosVoid, PosDiscount, PosPriceOverride, PosNoSale, PosReportsView,
    };

    /// <summary>Permissions that may carry a MaxPence ceiling on a grant.</summary>
    public static readonly IReadOnlySet<string> CeilingCapable = new HashSet<string>(StringComparer.Ordinal)
    {
        PosRefund, PosDiscount,
    };

    public static bool IsKnown(string code) => code != null && All.Contains(code);
}

/// <summary>One resolved permission: a code plus its effective ceiling (null = unlimited /
/// not amount-based). Union semantics: the HIGHEST ceiling wins; any unlimited grant wins
/// outright. Renders as "pos.refund.max:2000" per architecture §7.2.</summary>
public sealed record EffectivePermission(string Code, long? MaxPence)
{
    public override string ToString() => MaxPence.HasValue ? $"{Code}.max:{MaxPence}" : Code;

    /// <summary>Union-merge a set of grants: per code, unlimited beats any ceiling; otherwise
    /// the largest ceiling wins.</summary>
    public static IReadOnlyList<EffectivePermission> Merge(IEnumerable<EffectivePermission> grants) =>
        grants
            .GroupBy(g => g.Code, StringComparer.Ordinal)
            .Select(g => g.Any(x => x.MaxPence == null)
                ? new EffectivePermission(g.Key, null)
                : new EffectivePermission(g.Key, g.Max(x => x.MaxPence)))
            .OrderBy(g => g.Code, StringComparer.Ordinal)
            .ToList();
}
