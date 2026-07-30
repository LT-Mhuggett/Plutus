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
    /// <summary>Loyalty usability: create/edit customers, issue store credit, set membership.
    /// Surface-neutral (portal AND till) — a supervisor/manager holds it wherever they sign in.
    /// Deliberately distinct from portal.users.manage (staff admin) so front-line customer
    /// management is not tied to staff-user administration.</summary>
    public const string CustomersManage = "customers.manage";
    /// <summary>WP6.3: raise / read / reply to support tickets. Surface-neutral and seeded to EVERY
    /// built-in role — a lone cashier with a dead till must be able to shout for help.</summary>
    public const string SupportTickets = "support.tickets";

    // ── POS ──
    public const string PosSell = "pos.sell";
    public const string PosRefund = "pos.refund";           // ceiling-capable
    public const string PosVoid = "pos.void";
    public const string PosDiscount = "pos.discount";       // ceiling-capable
    public const string PosPriceOverride = "pos.price-override";
    public const string PosNoSale = "pos.no-sale";
    public const string PosReportsView = "pos.reports.view";
    /// <summary>WP6.3: manage the till's device-level settings (receipt behaviour, carrier-bag
    /// barcode, printer). Seeded to Owner / Company Admin / Store Manager.</summary>
    public const string PosSettingsManage = "pos.settings.manage";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        PortalFinancialsView, PortalUsersManage, PortalStockAdjust, PortalPricesManage,
        PortalTillsEnrol, PortalReportsView, PortalCompanyManage, CustomersManage, SupportTickets,
        PosSell, PosRefund, PosVoid, PosDiscount, PosPriceOverride, PosNoSale, PosReportsView, PosSettingsManage,
    };

    /// <summary>Permissions that may carry a MaxPence ceiling on a grant.</summary>
    public static readonly IReadOnlySet<string> CeilingCapable = new HashSet<string>(StringComparer.Ordinal)
    {
        PosRefund, PosDiscount,
    };

    /// <summary>WP14.1: permissions an IMPERSONATED session may never exercise, even if the target
    /// holds them — money movement + account/customer administration. Enforced BOTH by filtering
    /// the minted token's scopes AND in the permission handler (perm:* gates resolve from RBAC, not
    /// the token, so filtering alone is not enough).</summary>
    public static readonly IReadOnlySet<string> ImpersonationDenied = new HashSet<string>(StringComparer.Ordinal)
    {
        PosRefund, PosVoid, PortalUsersManage, PortalCompanyManage, CustomersManage,
    };

    public static bool IsKnown(string code) => code != null && All.Contains(code);

    /// <summary>FE9.3: which broad surface a permission belongs to — groups the roles reference and
    /// the per-user access matrix so a long flat list becomes readable.</summary>
    public static string GroupOf(string code) => code switch
    {
        CustomersManage => "Customers",
        SupportTickets => "Support",
        _ when code != null && code.StartsWith("portal.", StringComparison.Ordinal) => "Portal",
        _ when code != null && code.StartsWith("pos.", StringComparison.Ordinal) => "Till (POS)",
        _ => "Other",
    };

    /// <summary>
    /// FE9.3: plain-English descriptions, so "what does this role actually let someone do?" is
    /// answerable without reading the source. Code-defined for the same reason the catalogue is:
    /// a permission and its meaning ship together.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [PortalFinancialsView] = "See takings, banking and financial periods in the portal.",
        [PortalUsersManage] = "Add and edit staff users, set their passwords, and grant or remove roles.",
        [PortalStockAdjust] = "Adjust stock levels, run stock-takes and move stock between locations.",
        [PortalPricesManage] = "Change prices — the central price list and per-store overrides.",
        [PortalTillsEnrol] = "Create tills, issue enrolment codes, and revoke or un-enrol devices.",
        [PortalReportsView] = "View reports (sales, items sold, VAT, stock).",
        [PortalCompanyManage] = "Edit company and store details — names, addresses, opening hours, receipt template.",
        [CustomersManage] = "Add and edit customers, grant store credit, and set loyalty tiers. Works in the portal AND at the till.",
        [SupportTickets] = "Raise support tickets with the Plutus team and read the replies.",
        [PosSell] = "Ring up sales at the till.",
        [PosRefund] = "Give refunds. Can carry a per-refund money ceiling.",
        [PosVoid] = "Void a line or a whole transaction at the till.",
        [PosDiscount] = "Apply discounts. Can carry a per-discount money ceiling.",
        [PosPriceOverride] = "Override an item's price at the point of sale.",
        [PosNoSale] = "Open the cash drawer without a sale.",
        [PosReportsView] = "View reports on the till.",
        [PosSettingsManage] = "Change this till's device settings — receipt behaviour, carrier-bag barcode, printer.",
    };

    /// <summary>The description, or a readable fallback for a permission added without one.</summary>
    public static string DescribeOf(string code) =>
        code != null && Descriptions.TryGetValue(code, out var d) ? d : code ?? string.Empty;
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
