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

    /// <summary>
    /// Change an item's price or details FROM A TILL.
    ///
    /// ⚠⚠ Matt, 2026-08-21: *"I also need editing of items to be a supervisor and above permission
    /// across all tills."* Third instance of exactly the <see cref="PosStockAdjust"/> shape, and the
    /// third time for the same reason: a **Supervisor holds no portal permission at all**, so
    /// <see cref="PortalPricesManage"/> could never express "supervisor and above" — under it, a
    /// supervisor correcting a mispriced shelf edge has to wait for a manager.
    ///
    /// ⚠ Granting the PORTAL code to Supervisor instead would have been one line and the wrong shape:
    /// it also carries the central price list and per-store override writes. A till permission has to
    /// be expressed as a till permission.
    ///
    /// ⚠⚠ AND IT CLOSED A REAL HOLE, WHICH IS THE PART WORTH KEEPING. Before this existed the item
    /// write endpoints inherited a bare <c>[Authorize]</c> from the legacy CRUD base — **any signed-in
    /// user could create or edit any item**, including a Cashier, and the web till's item editor had
    /// **no client gate at all**. MAUI's gate was real but client-side only. So the ask was not just a
    /// widening for supervisors: it was the first server-side gate this capability has ever had.
    ///
    /// ⚠ Seeded to Owner / Company Admin / Store Manager / **Supervisor** — never Cashier. A price is
    /// what the customer is charged; a cashier changing one unsupervised is a discount with no reason,
    /// no ceiling and no audit row.
    ///
    /// ⚠ The endpoints accept this **OR** <see cref="PortalPricesManage"/> (the `CheckAny` shape), so
    /// portal users keep working unchanged and nothing needs re-granting.
    /// </summary>
    public const string PosItemsManage = "pos.items.manage";

    /// <summary>
    /// WP12 / step 27: sign a new loyalty member up FROM A TILL.
    ///
    /// ⚠ Matt, 2026-08-13: *"Supervisor to change tiers. Till operator to add new loyalty members."*
    /// (Binding default 20.) So this is the one customer capability that reaches the **Cashier** —
    /// and it has to, because signing someone up happens at the counter, mid-queue, while they are
    /// standing there. Making it wait for a supervisor is how a loyalty programme quietly stops
    /// being offered.
    ///
    /// ⚠ **CREATE-ONLY, DELIBERATELY.** Editing a member stays <see cref="CustomersManage"/>, and
    /// the split is not fussiness: changing an email quietly redirects somebody's account, and
    /// changing a tier changes every future basket they put through. Adding a row can be undone by
    /// deactivating it; altering one cannot be seen at all afterwards.
    ///
    /// ⚠ The create endpoint accepts **either** this or <see cref="CustomersManage"/> — the
    /// `CheckAny` shape from <see cref="PosStockAdjust"/> — so nobody who could already add a member
    /// loses the ability, and a till has ONE code to check whoever is signed in.
    ///
    /// ⚠ **Online-only on every till, and no permission can change that.** Member numbers come from
    /// a tenant-wide counter (`MemberNoAllocator`), so two offline tills would mint the same one.
    /// The gate says *who may*; the connection says *whether it is possible at all*.
    ///
    /// ⚠ Seeded to every selling role — Owner / Company Admin / Store Manager / Supervisor /
    /// **Cashier**. Also <see cref="ImpersonationDenied"/>: creating records inside a customer's
    /// tenant is not diagnosis, and it burns a number from their sequence.
    /// </summary>
    public const string PosCustomersAdd = "pos.customers.add";

    // ── POS reports, one code per report (ruling 5b, 2026-08-18) ──
    //
    // ⚠⚠ MATT: *"Separate permissions need to be created for viewing them."* Until now all eight
    // reports shared `pos.reports.view`, which is exactly why widening that gate for a Supervisor
    // widened it for EVERY report at once — and why the same gate defect has now been fixed four times.
    //
    // ⚠⚠ `pos.reports.view` REMAINS A MASTER KEY, and that is the whole compatibility story. Every
    // report endpoint accepts `pos.reports.view` OR its own code, so:
    //   - every existing role keeps working, unchanged, with no re-seed and no waiting for a token to
    //     expire (they cache 12h with the permission set baked in — pitfall 10);
    //   - a NARROW role is built by granting only the specific codes and NOT the master.
    // Making the specific codes mandatory instead would have logged every Supervisor out of every
    // report the moment this deployed, in a live shop, for a feature nobody had asked to be strict.
    //
    // ⚠ THE CODES ARE NAMED AFTER THE REPORT, not after a client's menu key. `ReportPermissions` maps
    // the two, so a till renaming a tab cannot silently change who may read it.
    //
    // ⚠ SEEDED TO NOBODY BY DEFAULT. A code no role holds grants nothing, which is the safe
    // direction: the narrow roles a shop wants are built in the portal, deliberately.
    public const string PosReportsTakings = "pos.reports.takings";
    public const string PosReportsVat = "pos.reports.vat";
    public const string PosReportsItemsSold = "pos.reports.items-sold";
    public const string PosReportsCategorySales = "pos.reports.category-sales";
    public const string PosReportsBestSellers = "pos.reports.best-sellers";

    /// <summary>
    /// On-hand stock, and the negative-stock view of it.
    ///
    /// ⚠⚠ ONE CODE FOR BOTH, DELIBERATELY. "Negative stock" is the SAME rows filtered to those
    /// below zero — it is a subset, so a role that could see stock but not negative stock would be
    /// nonsense, and one that could see negative stock but not stock would be shown the worst of the
    /// data and denied the context. They are one permission because they are one dataset.
    /// </summary>
    public const string PosReportsStock = "pos.reports.stock";

    /// <summary>
    /// The sales list and the drill-down into an individual sale.
    ///
    /// ⚠ ITS OWN CODE BECAUSE IT IS A DIFFERENT KIND OF LOOK. Every other report is an aggregate;
    /// this one shows individual transactions, what was in them and how each was paid. A shop may
    /// reasonably want a role that can read the day's takings without being able to open a customer's
    /// basket line by line.
    /// </summary>
    public const string PosReportsSales = "pos.reports.sales";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        PortalFinancialsView, PortalUsersManage, PortalStockAdjust, PortalPricesManage,
        PortalTillsEnrol, PortalReportsView, PortalCompanyManage, CustomersManage, SupportTickets,
        InventoryBulk, GiftCardsManage,
        PosSell, PosRefund, PosVoid, PosDiscount, PosPriceOverride, PosNoSale, PosReportsView, PosSettingsManage,
        PosStockAdjust, PosCashReopen, PosCustomersAdd, PosItemsManage,

        // ⚠ The per-report codes (5b). They must be here or the portal cannot offer them: `AdminController`
        // builds its grantable-permission list from this set, so a code missing from it exists in the
        // source and can never be given to anybody.
        PosReportsTakings, PosReportsVat, PosReportsItemsSold, PosReportsCategorySales,
        PosReportsBestSellers, PosReportsStock, PosReportsSales,
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
        // WP12: creating a member is not diagnosis, and it consumes a number from the tenant's own
        // sequence — consistent with CustomersManage above, which this list already denies for
        // "account/customer administration". ⚠ Easy to relax if support ever needs to add a member
        // on a shop's behalf; the reverse (discovering a support session minted customers) is not.
        PosCustomersAdd,
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
        [PosReportsView] = "View ALL of the till's reports. A master key — grant one of the individual report permissions instead to allow just that report.",

        // ⚠⚠ FE9.3 CAUGHT THESE MISSING. `Every_catalogue_permission_has_a_description_and_a_group`
        // fails on any code whose description is absent, because `DescribeOf` then echoes the raw code
        // back and the portal's grant list reads "pos.reports.category-sales" at somebody who is trying
        // to decide what a role should be allowed to do. A permission whose meaning nobody can read is
        // one nobody will grant correctly.
        //
        // ⚠ Each says WHAT THE REPORT SHOWS, not what it is called — the question being answered is
        // "what does this role actually let someone do?"
        [PosReportsTakings] = "View the till's takings — sales totals by day, with VAT and order counts.",
        [PosReportsVat] = "View the VAT report — net, VAT and gross broken down by rate.",
        [PosReportsItemsSold] = "View every line sold in a period, with quantities, which till rang it up and what it took.",
        [PosReportsCategorySales] = "View sales grouped by category, with each category's share of the takings.",
        [PosReportsBestSellers] = "View the best-selling items, ranked by quantity or by money taken.",
        [PosReportsStock] = "View on-hand stock, including the negative-stock report. Reading only — changing stock is a separate permission.",
        [PosReportsSales] = "Open individual sales — the sales list and the drill-down showing a sale's lines, how it was paid, and anything refunded against it.",
        [PosSettingsManage] = "Change this till's device settings — receipt behaviour, carrier-bag barcode, printer.",
        // ⚠ The wording says CHANGE, not count. The endpoint takes a signed delta, and somebody
        // reading this in the portal's role editor must not come away thinking it lets an operator
        // set a stock figure.
        [PosStockAdjust] = "Write stock off or add it back from a till — damaged, lost or found goods. Always needs a reason.",
        // ⚠ Says plainly that nothing is deleted: the fear this wording answers is "will I lose the
        // Z read?", and the honest answer is that both the close and the reopening stay on record.
        [PosCashReopen] = "Reopen a day that has been closed with a Z read, so the till can trade again. The Z read is kept — both the close and the reopening are recorded. Always needs a reason.",
        // ⚠ Says "from a till" and names the price, because in a role editor "manage items" reads
        // as a portal capability. The owner granting this needs to know it is the shelf-edge fix at
        // the counter, not the central price list.
        [PosItemsManage] = "Change an item's price or details from a till — the fix for a wrong shelf edge, without waiting for a manager. Does not include bulk catalogue edits or the central price list.",
        // ⚠ Spells out the create/edit line, because "add customers" in a role editor reads as
        // "manage customers" and this deliberately is not that. Also says the till must be online:
        // an owner granting it needs to know why a cashier still cannot do it on a dead connection.
        [PosCustomersAdd] = "Sign a new loyalty member up at the till. Adding only — changing a member's details or their tier needs the Customers permission. The till must be online, because membership numbers are issued centrally.",
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
