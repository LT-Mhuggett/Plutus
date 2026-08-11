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
    /// <summary>FE5.3: bulk catalogue edits (re-category, re-brand, move to the Bin) and the Bin
    /// view itself. Deliberately separate from portal.stock.adjust — one mistake here moves
    /// thousands of items, so it is granted to Owner / Company Admin / Store Manager only.</summary>
    public const string InventoryBulk = "inventory.bulk";
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
    /// <summary>FE7: gift-card administration — generate codes, void/un-void, adjust a balance, link a
    /// card to a customer, and read the card list. NOT needed to sell or take a card at the till
    /// (that's pos.sell): a cashier sells cards all day, but minting codes and moving balances by hand
    /// is a manager's job. Seeded to Owner / Company Admin / Store Manager.</summary>
    public const string GiftCardsManage = "giftcards.manage";

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

    /// <summary>
    /// WP10 / cutover step 25: correct a stock count or write stock off FROM A TILL.
    ///
    /// ⚠ IT IS A SEPARATE CODE FROM <see cref="PortalStockAdjust"/> ON PURPOSE, and the reason is
    /// the whole point of the decision behind it (Matt, 2026-08-11). The endpoints accept EITHER,
    /// so a manager needs nothing new — but a **Supervisor** holds no portal permission at all, and
    /// under the portal code alone a supervisor standing at the counter with a damaged box could
    /// not write it off. Waiting for a manager to come in and adjust it is how a shop ends up with
    /// stock figures nobody trusts.
    ///
    /// ⚠ Granting the PORTAL code to Supervisor instead would have been one line and the wrong
    /// shape: it also carries category create/rename/delete and price-list writes in the portal.
    /// A till permission has to be expressed as a till permission.
    ///
    /// ⚠ Seeded to Owner / Company Admin / Store Manager / **Supervisor** — never Cashier. A
    /// cashier changing stock counts unsupervised is how shrinkage stops being visible.
    /// </summary>
    public const string PosStockAdjust = "pos.stock.adjust";

    /// <summary>
    /// Reverse a Z close so the day can trade again.
    ///
    /// ⚠ Matt, 2026-08-11: *"A supervisor or above needs to be able to reverse the close."* Same
    /// shape as <see cref="PosStockAdjust"/> and for the same reason: it is a TILL action, taken at
    /// the counter, by somebody who holds no portal permission at all. Expressing it as a portal
    /// code would mean a supervisor could not do the one thing this exists for.
    ///
    /// ⚠ Seeded to Owner / Company Admin / Store Manager / **Supervisor** — never Cashier. Closing
    /// the day is a statement about counted money; reopening it makes that statement editable, and
    /// the person who counted the drawer must not be the only one who can quietly un-count it.
    ///
    /// ⚠ IT MOVES NO MONEY. Reopening changes no float, no takings and no counted figure — it makes
    /// the day writable again, and leaves both the close and the reopen on the record.
    /// </summary>
    public const string PosCashReopen = "pos.cash.reopen";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        PortalFinancialsView, PortalUsersManage, PortalStockAdjust, PortalPricesManage,
        PortalTillsEnrol, PortalReportsView, PortalCompanyManage, CustomersManage, SupportTickets,
        InventoryBulk, GiftCardsManage,
        PosSell, PosRefund, PosVoid, PosDiscount, PosPriceOverride, PosNoSale, PosReportsView, PosSettingsManage,
        PosStockAdjust, PosCashReopen,
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
        // FE7: an impersonated session must not be able to mint gift cards or move balances — that is
        // money creation, which is exactly what this list exists to keep out of support sessions.
        GiftCardsManage,
    };

    public static bool IsKnown(string code) => code != null && All.Contains(code);

    /// <summary>FE9.3: which broad surface a permission belongs to — groups the roles reference and
    /// the per-user access matrix so a long flat list becomes readable.</summary>
    public static string GroupOf(string code) => code switch
    {
        CustomersManage => "Customers",
        GiftCardsManage => "Customers",
        SupportTickets => "Support",
        InventoryBulk => "Portal",
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
        [InventoryBulk] = "Bulk-edit the catalogue — change category or brand on many items at once, move items to the Bin, and see/restore the Bin.",
        [PortalPricesManage] = "Change prices — the central price list and per-store overrides.",
        [PortalTillsEnrol] = "Create tills, issue enrolment codes, and revoke or un-enrol devices.",
        [PortalReportsView] = "View reports (sales, items sold, VAT, stock).",
        [PortalCompanyManage] = "Edit company and store details — names, addresses, opening hours, receipt template.",
        [CustomersManage] = "Add and edit customers, grant store credit, and set loyalty tiers. Works in the portal AND at the till.",
        [SupportTickets] = "Raise support tickets with the Plutus team and read the replies.",
        [GiftCardsManage] = "Generate gift-card codes, see every card and its balance, void or reinstate a card, correct a balance by hand, and link a card to a customer. Selling and taking gift cards at the till only needs 'Ring up sales'.",
        [PosSell] = "Ring up sales at the till.",
        [PosRefund] = "Give refunds. Can carry a per-refund money ceiling.",
        [PosVoid] = "Void a line or a whole transaction at the till.",
        [PosDiscount] = "Apply discounts. Can carry a per-discount money ceiling.",
        [PosPriceOverride] = "Override an item's price at the point of sale.",
        [PosNoSale] = "Open the cash drawer without a sale.",
        [PosReportsView] = "View reports on the till.",
        [PosSettingsManage] = "Change this till's device settings — receipt behaviour, carrier-bag barcode, printer.",
        // ⚠ The wording says CHANGE, not count. The endpoint takes a signed delta, and somebody
        // reading this in the portal's role editor must not come away thinking it lets an operator
        // set a stock figure.
        [PosStockAdjust] = "Write stock off or add it back from a till — damaged, lost or found goods. Always needs a reason.",
        // ⚠ Says plainly that nothing is deleted: the fear this wording answers is "will I lose the
        // Z read?", and the honest answer is that both the close and the reopening stay on record.
        [PosCashReopen] = "Reopen a day that has been closed with a Z read, so the till can trade again. The Z read is kept — both the close and the reopening are recorded. Always needs a reason.",
    };

    /// <summary>The description, or a readable fallback for a permission added without one.</summary>
    public static string DescribeOf(string code) =>
        code != null && Descriptions.TryGetValue(code, out var d) ? d : code ?? string.Empty;
}

/// <summary>
/// One grant as it is STORED — a permission, its ceiling, and the window it applies in.
///
/// ⚠ WHY THE WINDOW TRAVELS WITH IT. A till downloads its roster and then runs offline for days.
/// If the server resolved "is this operator allowed right now" at download time and shipped the
/// answer, a Saturday-only supervisor synced on a Wednesday would have NO permissions until the
/// next sync — permanently, and silently. The window has to be evaluated against the TILL's clock,
/// at the moment of the action, which means the raw fields have to reach the till.
/// </summary>
/// <param name="DaysOfWeekMask">Bit 0 = Sunday … bit 6 = Saturday; null = any day.</param>
/// <param name="WindowStartLocal">⚠ LOCAL wall-clock, while ValidFrom/To are UTC INSTANTS. Mixing
/// them is the bug this type exists to make hard.</param>
public sealed record PermissionGrant(
    string Code,
    long? MaxPence,
    DateTime? ValidFromUtc = null,
    DateTime? ValidToUtc = null,
    byte? DaysOfWeekMask = null,
    TimeOnly? WindowStartLocal = null,
    TimeOnly? WindowEndLocal = null)
{
    /// <summary>
    /// Is this grant live at the given moment?
    ///
    /// ⚠ THE ONE IMPLEMENTATION, used by the server's RBAC resolver AND by every till. A second
    /// copy would be a permission that means one thing centrally and another on a counter — the
    /// exact class of drift till-design Part C exists to prevent.
    ///
    /// ⚠ A window that wraps midnight (22:00–02:00) is NOT supported: it would reject everything.
    /// Deliberately unhandled rather than half-handled — a night-shift window that silently denied
    /// every action would be worse than one nobody can save in the first place.
    /// </summary>
    public bool IsActiveAt(DateTime nowLocal)
    {
        var nowUtc = nowLocal.Kind == DateTimeKind.Utc ? nowLocal : nowLocal.ToUniversalTime();
        if (ValidFromUtc.HasValue && nowUtc < ValidFromUtc.Value) return false;
        if (ValidToUtc.HasValue && nowUtc > ValidToUtc.Value) return false;

        if (DaysOfWeekMask.HasValue &&
            (DaysOfWeekMask.Value & (1 << (int)nowLocal.DayOfWeek)) == 0) return false;

        if (WindowStartLocal.HasValue || WindowEndLocal.HasValue)
        {
            var t = TimeOnly.FromDateTime(nowLocal);
            if (WindowStartLocal.HasValue && t < WindowStartLocal.Value) return false;
            if (WindowEndLocal.HasValue && t > WindowEndLocal.Value) return false;
        }
        return true;
    }
}

/// <summary>Turning stored grants into what an operator may do right now.</summary>
public static class PermissionResolution
{
    /// <summary>Filter to the grants live at <paramref name="nowLocal"/>, then union-merge them.
    /// This is what a till runs at the moment of an action, against its own clock.</summary>
    public static IReadOnlyList<EffectivePermission> EffectiveAt(
        IEnumerable<PermissionGrant> grants, DateTime nowLocal) =>
        EffectivePermission.Merge(
            grants.Where(g => g.IsActiveAt(nowLocal)).Select(g => new EffectivePermission(g.Code, g.MaxPence)));

    /// <summary>
    /// May this operator do <paramref name="code"/>, for <paramref name="amountPence"/>?
    ///
    /// ⚠ FAILS CLOSED, including on an unknown permission code. <c>"perm:x"</c> and
    /// <c>PlutusPolicies.X</c> are different namespaces and a typo between them has already caused
    /// one silent outage here (the pick-notes gate) — so an unrecognised code is denied, never
    /// waved through.
    ///
    /// ⚠ A ceiling applies to the AMOUNT, so a null amount against a ceiling-bearing grant is
    /// allowed: "may refund at all" and "may refund £40" are different questions.
    /// </summary>
    public static bool Can(
        IEnumerable<PermissionGrant> grants, string code, DateTime nowLocal, long? amountPence = null)
    {
        if (string.IsNullOrEmpty(code)) return false;

        foreach (var p in EffectiveAt(grants, nowLocal))
        {
            if (!string.Equals(p.Code, code, StringComparison.Ordinal)) continue;
            if (amountPence is null || p.MaxPence is null) return true;   // unlimited, or not amount-based
            if (amountPence.Value <= p.MaxPence.Value) return true;
        }
        return false;
    }
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
