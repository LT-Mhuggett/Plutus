using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;

// ─────────────────────────────────────────────────────────────────────────────
// Plutus.SeedMigrator — one-off ETL: NatApp-era till backup (old Plutus.Database
// schema, SQLite) → new Plutus.Entities schema (MySQL, for the webapp test env).
// See WebApp-2026-07-23-plan.md §2.4 for the mapping rationale.
//
// Usage:
//   Plutus.SeedMigrator <old-backup.db> --dry-run
//   Plutus.SeedMigrator <old-backup.db> --mysql "<connection string>"
//
// Deliberately NOT ported: Employees.HashedPassword/Salt (no credential fields on
// the new client-side Employee; see OfflineMode plan §4.3) and SavedTransactions
// (device-local basket state).
// ─────────────────────────────────────────────────────────────────────────────

const string SeedUser = "seed-migrator";

if (args.Length < 2) { Console.Error.WriteLine("usage: <old.db> --dry-run | --mysql <connstring>"); return 1; }

// ── WP3.1 runner: seed RBAC built-in roles + Kapow AuthActions mapping. ──
//   Plutus.SeedMigrator rbac --mysql "<connstring>"     (idempotent; re-run = no-op)
if (args[0] == "rbac")
{
    var rbacMysqlIdx = Array.IndexOf(args, "--mysql");
    if (rbacMysqlIdx < 0 || rbacMysqlIdx + 1 >= args.Length)
    { Console.Error.WriteLine("rbac needs --mysql <conn>"); return 1; }

    var rbacTenant = Plutus.Entities.Tenancy.KnownTenants.Kapow;
    var rbacOptions = new DbContextOptionsBuilder<MySqlDbContext>()
        .UseMySql(args[rbacMysqlIdx + 1], MySqlServerVersion.LatestSupportedServerVersion);
    using var rbacDb = new MySqlDbContext(rbacOptions.Options, new Plutus.Entities.Tenancy.FixedTenantContext(rbacTenant));
    var (roles, assignments) = await Plutus.Identity.RbacSeeder.SeedAsync(rbacDb, rbacTenant);
    Console.WriteLine($"rbac seed: {roles} role(s) added, {assignments} assignment(s) added.");
    return 0;
}

// ── WP3.3 runner: rebuild the reporting rollups from SalesV2 (idempotent). ──
//   Plutus.SeedMigrator rollups-rebuild --mysql "<connstring>"
if (args[0] == "rollups-rebuild")
{
    var rrIdx = Array.IndexOf(args, "--mysql");
    if (rrIdx < 0 || rrIdx + 1 >= args.Length)
    { Console.Error.WriteLine("rollups-rebuild needs --mysql <conn>"); return 1; }

    var rrTenant = Plutus.Entities.Tenancy.KnownTenants.Kapow;
    var rrOptions = new DbContextOptionsBuilder<MySqlDbContext>()
        .UseMySql(args[rrIdx + 1], MySqlServerVersion.LatestSupportedServerVersion);
    using var rrDb = new MySqlDbContext(rrOptions.Options, new Plutus.Entities.Tenancy.FixedTenantContext(rrTenant));
    var (sr, vr, scanned) = await Plutus.Reporting.RollupRebuilder.RebuildAsync(rrDb, rrTenant);
    Console.WriteLine($"rollups rebuilt: {sr} SalesRollups + {vr} VatRollups from {scanned} sales.");
    return 0;
}

// ── WP5.1 runner: seed stock-ledger opening balances from the legacy Stocks table. ──
//   Plutus.SeedMigrator stock-open --mysql "<connstring>"     (idempotent per item)
if (args[0] == "stock-open")
{
    var soIdx = Array.IndexOf(args, "--mysql");
    if (soIdx < 0 || soIdx + 1 >= args.Length)
    { Console.Error.WriteLine("stock-open needs --mysql <conn>"); return 1; }

    var soTenant = Plutus.Entities.Tenancy.KnownTenants.Kapow;
    var soOptions = new DbContextOptionsBuilder<MySqlDbContext>()
        .UseMySql(args[soIdx + 1], MySqlServerVersion.LatestSupportedServerVersion);
    using var soDb = new MySqlDbContext(soOptions.Options, new Plutus.Entities.Tenancy.FixedTenantContext(soTenant));
    var opened = await Plutus.Catalogue.StockRebuilder.SeedOpeningBalancesAsync(soDb, soTenant);
    Console.WriteLine($"stock-open: {opened} opening-balance movement(s) added.");
    return 0;
}

var oldDbPath = args[0];
var dryRun = args[1] == "--dry-run";
var mysqlConn = dryRun ? null : args[2 == args.Length ? 1 : 2];
if (!dryRun && args[1] == "--mysql") mysqlConn = args[2];
if (!File.Exists(oldDbPath)) { Console.Error.WriteLine($"not found: {oldDbPath}"); return 1; }

// ── T1.8 runner: Kapow → sales-v2. Reuses the Plutus.Migration.Kapow library. ──
//   Plutus.SeedMigrator <kapow.db> sales-v2 --sqlite <out.db>                (validation run)
//   Plutus.SeedMigrator <kapow.db> sales-v2 --mysql "<connstring>" --verify  (what WOULD import)
//   Plutus.SeedMigrator <kapow.db> sales-v2 --mysql "<connstring>" --apply   (delta import)
//
// ⚠⚠ INCREMENTAL SINCE 2026-08-17 (translation plan §3.2). The original one-shot form assumed an
// empty target and minted a fresh random till/device id per invocation — so the only safe re-run
// was "drop every LegacyRef sale and reload", which re-mints every sale's UUID and breaks anything
// that captured one (a refund's origin id, a receipt barcode). This form reads the identity the
// FIRST run stamped, skips every sale the target already has (by LegacyRef — the NatApp Sales.Id),
// and numbers new sales after the existing DeviceSeq high-water mark. Same backup twice = no-op.
if (Array.IndexOf(args, "sales-v2") >= 0)
{
    var tenantId = Plutus.Entities.Tenancy.KnownTenants.Kapow;
    var verify = Array.IndexOf(args, "--verify") >= 0;
    var apply = Array.IndexOf(args, "--apply") >= 0;

    var tenantCtx = new Plutus.Entities.Tenancy.FixedTenantContext(tenantId);
    var sqliteIdx = Array.IndexOf(args, "--sqlite");
    var mysqlIdx = Array.IndexOf(args, "--mysql");
    var options = new DbContextOptionsBuilder<MySqlDbContext>();
    if (sqliteIdx >= 0 && sqliteIdx + 1 < args.Length)
    {
        var outPath = args[sqliteIdx + 1];
        if (File.Exists(outPath)) File.Delete(outPath);
        options.UseSqlite($"Data Source={outPath}");
        apply = apply || !verify;   // a scratch sqlite target keeps the old write-by-default
    }
    else if (mysqlIdx >= 0 && mysqlIdx + 1 < args.Length)
    {
        options.UseMySql(args[mysqlIdx + 1], MySqlServerVersion.LatestSupportedServerVersion);
        // ⚠ A MYSQL TARGET IS SOMEBODY'S LIVE (or their rehearsal of live): writing is opt-in.
        // The TenantRestore convention — verify prints what an apply WOULD do and touches nothing.
        if (!verify && !apply)
        { Console.Error.WriteLine("sales-v2 --mysql needs --verify (read-only) or --apply (write)"); return 1; }
    }
    else { Console.Error.WriteLine("sales-v2 needs --sqlite <out.db> or --mysql <conn>"); return 1; }

    using var target = new MySqlDbContext(options.Options, tenantCtx);
    target.Database.EnsureCreated();

    // ── Identity: reuse what the first run stamped; mint only on a genuinely empty target. ──
    // ⚠ The 21,646 historical rows all carry ONE (tillId, deviceId) pair — reading it back is
    // what makes the unique index (TenantId, DeviceId, DeviceSeq) an idempotency key instead of
    // a booby trap, and keeps the whole NatApp history attributable as one till.
    var stamped = target.SalesV2.IgnoreQueryFilters().AsNoTracking()
        .Where(s => s.TenantId == tenantId && s.LegacyRef != null)
        .Select(s => new { s.TillId, s.DeviceId })
        .FirstOrDefault();
    var legacyTillId = stamped?.TillId ?? Guid.NewGuid();
    var legacyDeviceId = stamped?.DeviceId ?? Guid.NewGuid();
    Console.WriteLine(stamped != null
        ? $"target already holds legacy sales for till {legacyTillId} / device {legacyDeviceId} — reusing"
        : $"target holds no legacy sales — minting till {legacyTillId} / device {legacyDeviceId}");

    using var kapowConn = new SqliteConnection(
        new SqliteConnectionStringBuilder { DataSource = oldDbPath, Mode = SqliteOpenMode.ReadOnly }.ToString());
    kapowConn.Open();
    var inputs = new Plutus.Migration.Kapow.KapowSalesReader(kapowConn).Read(tenantId, legacyTillId, legacyDeviceId);
    Console.WriteLine($"read {inputs.Count} Kapow sales from the backup; computing delta…");

    // ── Delta: skip what the target already has (recorded or quarantined). ──
    var recordedRefs = target.SalesV2.IgnoreQueryFilters().AsNoTracking()
        .Where(s => s.TenantId == tenantId && s.LegacyRef != null)
        .Select(s => s.LegacyRef!)
        .ToHashSet(StringComparer.Ordinal);
    // ⚠ SaleQuarantine.PayloadJson holds the raw LegacyId — that is what the migrator writes there.
    var quarantinedRefs = target.SaleQuarantine.IgnoreQueryFilters().AsNoTracking()
        .Where(q => q.TenantId == tenantId)
        .Select(q => q.PayloadJson)
        .ToHashSet(StringComparer.Ordinal);
    var maxSeq = target.SalesV2.IgnoreQueryFilters().AsNoTracking()
        .Where(s => s.TenantId == tenantId && s.DeviceId == legacyDeviceId)
        .Select(s => (long?)s.DeviceSeq).Max() ?? 0;

    var plan = Plutus.Migration.Kapow.KapowDelta.Plan(inputs, recordedRefs, quarantinedRefs, maxSeq);
    Console.WriteLine(plan);
    if (plan.ToImport.Count == 0) { Console.WriteLine("nothing to import — target is already current."); return 0; }

    // ── WP3.4 guardrail: refuse to write into a CLOSED financial period. ──
    // ⚠ The one incident this tool family has actually caused (£44k mis-posted) was an ETL running
    // after a period close. Sales for closed days must not be silently back-posted; a human reopens
    // the period first or decides the sales belong elsewhere. Checked against the DELTA's dates,
    // not the backup's (history already in the target doesn't matter here).
    var minDay = DateOnly.Parse(plan.MinDate![..10]);
    var maxDay = DateOnly.Parse(plan.MaxDate![..10]);
    var closed = target.FinancialPeriods.IgnoreQueryFilters().AsNoTracking()
        .Where(p => p.TenantId == tenantId && p.Status == Plutus.Entities.Models.PeriodStatus.Closed
                 && p.StartDay <= maxDay && p.EndDay >= minDay)
        .Select(p => p.Name)
        .ToList();
    if (closed.Count > 0)
    {
        Console.Error.WriteLine(
            $"REFUSED: the delta ({minDay}→{maxDay}) overlaps closed financial period(s): " +
            string.Join(", ", closed) + ". Reopen the period(s) first, or resolve which days these sales belong to.");
        return 1;
    }

    var recon = Plutus.Migration.Kapow.KapowMigrator.Migrate(
        plan.ToImport, target, new Plutus.Migration.Kapow.IdRemap<string>(), log: Console.WriteLine,
        write: apply && !verify, knownQuarantinedRefs: quarantinedRefs);
    Console.WriteLine();
    Console.WriteLine(verify ? "VERIFY ONLY — nothing was written. An --apply would record:" : "applied:");
    Console.WriteLine(recon);
    if (recon.QuarantineReasons.Count > 0)
    {
        Console.WriteLine("sample quarantine reasons:");
        foreach (var q in recon.QuarantineReasons) Console.WriteLine("  - " + q);
    }
    return 0;
}

// ── items-delta runner: INSERT-ONLY catalogue top-up for a NEWER backup (plan §3.2's
//    "upsert-by-old-id", scoped to inserts — 2026-08-17). ──
//   Plutus.SeedMigrator <kapow.db> items-delta --mysql "<conn>" --verify|--apply
//
// ⚠ WHY IT EXISTS: a delta SALES import can reference items created on the till after the
// original seed — sale lines join Items by barcode for their NAME, so without the item row those
// lines report as bare barcodes and the item can never be sold at a Plutus till. This inserts
// ONLY items whose (IdOne, IdTwo) is absent (and any missing category they point at), with the
// same mapping conventions as the original loader: TaxId = the file's VatId (lifted 1:1 by the
// seed), CatId = DetGuid("category", oldId), StrE coercions, >20-char ids skipped, CI dedupe.
//
// ⚠⚠ IT NEVER UPDATES. The portal owns the catalogue now (WP6.1): an item whose price moved on
// the OLD till after the seed keeps its portal value here, and the difference is REPORTED so a
// human decides. "Do NOT overwrite anything that is new" — Matt, 2026-08-17.
if (Array.IndexOf(args, "items-delta") >= 0)
{
    var verify = Array.IndexOf(args, "--verify") >= 0;
    var apply = Array.IndexOf(args, "--apply") >= 0;
    var mysqlIdx = Array.IndexOf(args, "--mysql");
    if (mysqlIdx < 0 || mysqlIdx + 1 >= args.Length || (!verify && !apply))
    { Console.Error.WriteLine("items-delta needs --mysql <conn> and --verify or --apply"); return 1; }

    var idTenant = Plutus.Entities.Tenancy.KnownTenants.Kapow;
    var idBusiness = DetGuid("business", "kapow");
    var idOptions = new DbContextOptionsBuilder<MySqlDbContext>()
        .UseMySql(args[mysqlIdx + 1], MySqlServerVersion.LatestSupportedServerVersion);
    using var idDb = new MySqlDbContext(idOptions.Options, new Plutus.Entities.Tenancy.FixedTenantContext(idTenant));
    idDb.CurrentUser = "items-delta";

    using var idConn = new SqliteConnection(
        new SqliteConnectionStringBuilder { DataSource = oldDbPath, Mode = SqliteOpenMode.ReadOnly }.ToString());
    idConn.Open();
    IEnumerable<System.Data.IDataRecord> IdRows(string sql)
    {
        using var cmd = idConn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        while (r.Read()) yield return r;
    }
    static string? IdStr(object v) => v is null or DBNull ? null : Convert.ToString(v);
    static string IdStrE(object v) { var s = IdStr(v); return string.IsNullOrWhiteSpace(s) ? "-" : s; }
    static decimal IdDec(object v) => v is null or DBNull ? 0m : decimal.Parse(Convert.ToString(v)!, CultureInfo.InvariantCulture);

    var haveItems = idDb.Items.IgnoreQueryFilters().AsNoTracking()
        .Where(i => i.IdTwo == idBusiness).Select(i => i.IdOne).ToList()
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var haveCats = idDb.Category.IgnoreQueryFilters().AsNoTracking()
        .Where(c => c.IdTwo == idBusiness).Select(c => c.IdOne).ToList().ToHashSet();

    // ⚠ Only barcodes a RECORDED sale line actually references — the sales made them matter.
    // Unreferenced till-side items wait for the full-catalogue cutover import (plan step 4/9).
    var referenced = idDb.SaleLines.IgnoreQueryFilters().AsNoTracking()
        .Where(l => l.TenantId == idTenant && l.ItemIdOne != null)
        .Select(l => l.ItemIdOne!).Distinct().ToList()
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var fileCats = new Dictionary<string, (string Name, string? Desc)>();
    foreach (var r in IdRows("SELECT Id, Name, Description FROM Category"))
        fileCats[IdStr(r["Id"])!] = (IdStrE(r["Name"]), IdStr(r["Description"]));

    var newItems = new List<Item>();
    var newCats = new Dictionary<Guid, Category>();
    var skippedUnreferenced = 0;
    foreach (var r in IdRows("SELECT * FROM Items"))
    {
        var id = IdStr(r["Id"]);
        if (id == null || id.Length > 20) continue;
        if (haveItems.Contains(id)) continue;
        if (!referenced.Contains(id)) { skippedUnreferenced++; continue; }

        var catOld = IdStr(r["CatId"]) ?? "";
        var catId = DetGuid("category", catOld);
        if (!haveCats.Contains(catId) && !newCats.ContainsKey(catId) && fileCats.TryGetValue(catOld, out var fc))
            newCats[catId] = new Category { IdOne = catId, IdTwo = idBusiness, Name = fc.Name, Description = fc.Desc };

        newItems.Add(new Item
        {
            IdOne = id, IdTwo = idBusiness,
            // ⚠ Desc has no [Required] but the COLUMN is NOT NULL — a null here fails at MySQL,
            // not at validation (found on the t1 rehearsal, which is what rehearsals are for).
            Name = IdStrE(r["Name"]), Brand = IdStrE(r["Brand"]), Desc = IdStr(r["Desc"]) ?? "",
            Cost = IdDec(r["Cost"]), ExPrice = IdDec(r["ExPrice"]), Price = IdDec(r["Price"]),
            TaxId = r["VatId"] is null or DBNull ? 0 : Convert.ToInt32(r["VatId"]), CatId = catId,
        });
        haveItems.Add(id);
    }

    Console.WriteLine($"items-delta: {newItems.Count} missing item(s) referenced by recorded sales; " +
        $"{newCats.Count} missing categorie(s); {skippedUnreferenced} new-but-unreferenced left for the cutover import.");
    foreach (var i in newItems) Console.WriteLine($"  + {i.IdOne}  {i.Name}  £{i.Price:0.00} (tax {i.TaxId})");
    if (!apply || verify) { Console.WriteLine("VERIFY ONLY — nothing was written."); return 0; }

    foreach (var c in newCats.Values) idDb.Category.Add(c);
    foreach (var i in newItems) idDb.Items.Add(i);
    idDb.SaveChanges();
    Console.WriteLine($"applied: {newItems.Count} item(s), {newCats.Count} categorie(s) inserted. Nothing updated.");
    return 0;
}

// ── replace-from-backup runner: FULL REPLACE of imported sales + inventory from a fresh NatApp
//    backup, plus the one-time purges Matt named (translation plan §8, 2026-08-19). ──
//   Plutus.SeedMigrator <kapow.db> replace-from-backup --mysql "<conn>" --verify|--apply
//
// ⚠⚠ MATT: *"I need all sales from the original import deleted and all new from the new data. I need
// all Inventory from the original import deleted and all new from the new data. Banking data can be
// dropped… Any giftcards can be dropped… Anything else new can stay e.g. Users & Roles, Locations &
// Tills, Company."* This is NOT the bridge run: a bridge tops up, this REPLACES — one consistent
// source (the newest backup) instead of layers of deltas.
//
// ⚠⚠ WHAT IT NEVER TOUCHES, by construction, not by care: platform-native sales (LegacyRef IS NULL —
// webstore orders are REAL MONEY), customers/memberships/credit, employees/RBAC, stores/tills/devices,
// themes, report publications, PaymentEvents (webstore reconciliation — not "banking"), and the
// provisioned items the backup cannot know (GIFT-CARD, BAG-%). GiftCardSettings stays too: it is the
// tenant's chosen configuration, not a card.
//
// ⚠⚠ THE IDENTITY IS CAPTURED BEFORE THE PURGE. sales-v2 reads its till/device from the target's own
// LegacyRef rows; run it AFTER a purge and it would mint a fresh random pair — recreating the exact
// orphaned-till defect §3.1 exists to prevent. That is why the reimport happens INSIDE this command
// rather than as a second invocation.
if (Array.IndexOf(args, "replace-from-backup") >= 0)
{
    var rfVerify = Array.IndexOf(args, "--verify") >= 0;
    var rfApply = Array.IndexOf(args, "--apply") >= 0;
    var rfMysqlIdx = Array.IndexOf(args, "--mysql");
    if (rfMysqlIdx < 0 || rfMysqlIdx + 1 >= args.Length || (!rfVerify && !rfApply) || (rfVerify && rfApply))
    { Console.Error.WriteLine("replace-from-backup needs --mysql <conn> and exactly one of --verify | --apply"); return 1; }

    var rfTenant = Plutus.Entities.Tenancy.KnownTenants.Kapow;
    var rfBusiness = DetGuid("business", "kapow");
    var rfOptions = new DbContextOptionsBuilder<MySqlDbContext>()
        .UseMySql(args[rfMysqlIdx + 1], MySqlServerVersion.LatestSupportedServerVersion);
    using var rfDb = new MySqlDbContext(rfOptions.Options, new Plutus.Entities.Tenancy.FixedTenantContext(rfTenant));
    rfDb.CurrentUser = "replace-from-backup";
    rfDb.Database.SetCommandTimeout(600);   // 20k items + 80k lines in one transaction is not a 30s job

    using var rfConn = new SqliteConnection(
        new SqliteConnectionStringBuilder { DataSource = oldDbPath, Mode = SqliteOpenMode.ReadOnly }.ToString());
    rfConn.Open();
    IEnumerable<System.Data.IDataRecord> RfRows(string sql)
    {
        using var cmd = rfConn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        while (r.Read()) yield return r;
    }
    static string? RfStr(object v) => v is null or DBNull ? null : Convert.ToString(v);
    static string RfStrE(object v) { var s = RfStr(v); return string.IsNullOrWhiteSpace(s) ? "-" : s; }
    static decimal RfDec(object v) => v is null or DBNull ? 0m : decimal.Parse(Convert.ToString(v)!, CultureInfo.InvariantCulture);
    static byte[]? RfBlob(object v) => v is null or DBNull ? null : (byte[])v;

    // ── 0. Identity, BEFORE anything is deleted — see the header. ──
    var rfStamped = rfDb.SalesV2.IgnoreQueryFilters().AsNoTracking()
        .Where(s => s.TenantId == rfTenant && s.LegacyRef != null)
        .Select(s => new { s.TillId, s.DeviceId })
        .FirstOrDefault();
    if (rfStamped is null)
    {
        // ⚠ Refuse rather than mint: with no imported sales there is nothing to replace, and minting a
        // random identity here is the one thing this command must never do. A first-ever import is the
        // plain sales-v2 path's job.
        Console.Error.WriteLine("REFUSED: the target holds no LegacyRef sales, so there is nothing to replace " +
            "(and no stamped till/device identity to reuse). For a first import use sales-v2.");
        return 1;
    }
    Console.WriteLine($"identity captured: till {rfStamped.TillId} / device {rfStamped.DeviceId}");

    // ── 1. Safety: nothing kept may point at a sale the purge deletes. ──
    // ⚠ Reimported sales get FRESH UUIDs (the tool's own header warns exactly this), so any platform
    // row referencing an imported sale's id would be orphaned. Today both counts are zero — Matt's
    // refund tests were against platform test sales. If either ever fires, a human MUST look; there is
    // deliberately no override flag, because "no questions" is only safe while the answer is zero.
    var rfOrphanAdjustments = rfDb.SaleAdjustments.IgnoreQueryFilters().AsNoTracking()
        .Count(a => a.TenantId == rfTenant && rfDb.SalesV2.Any(s =>
            s.TenantId == rfTenant && s.LegacyRef != null &&
            (s.Id == a.OriginalSaleId || s.Id == a.AdjustmentSaleId)));
    var rfOrphanCredits = rfDb.CreditEntries.IgnoreQueryFilters().AsNoTracking()
        .Count(e => e.TenantId == rfTenant && e.SaleId != null && rfDb.SalesV2.Any(s =>
            s.TenantId == rfTenant && s.LegacyRef != null && s.Id == e.SaleId));
    if (rfOrphanAdjustments > 0 || rfOrphanCredits > 0)
    {
        Console.Error.WriteLine($"REFUSED: {rfOrphanAdjustments} sale adjustment(s) and {rfOrphanCredits} " +
            "credit entrie(s) reference imported sales. Purging would orphan them — resolve by hand first.");
        return 1;
    }

    // ── 2. The before picture — every figure the report needs, read while it still exists. ──
    var rfProtectedGift = Plutus.SharedKernel.GiftCards.ItemIdOne;
    var rfProtectedBagPrefix = Plutus.SharedKernel.CarrierBags.IdPrefix + "%";

    int CLegacySales() => rfDb.SalesV2.IgnoreQueryFilters().Count(s => s.TenantId == rfTenant && s.LegacyRef != null);
    int CPlatformSales() => rfDb.SalesV2.IgnoreQueryFilters().Count(s => s.TenantId == rfTenant && s.LegacyRef == null);
    int CItems() => rfDb.Items.IgnoreQueryFilters().Count(i => i.IdTwo == rfBusiness);
    int CProtected() => rfDb.Items.IgnoreQueryFilters().Count(i => i.IdTwo == rfBusiness &&
        (i.IdOne == rfProtectedGift || EF.Functions.Like(i.IdOne, rfProtectedBagPrefix)));

    var before = new
    {
        LegacySales = CLegacySales(),
        PlatformSales = CPlatformSales(),
        Lines = rfDb.SaleLines.IgnoreQueryFilters().Count(l => l.TenantId == rfTenant),
        Tenders = rfDb.SaleTenders.IgnoreQueryFilters().Count(t => t.TenantId == rfTenant),
        Quarantine = rfDb.SaleQuarantine.IgnoreQueryFilters().Count(q => q.TenantId == rfTenant),
        Items = CItems(), Protected = CProtected(),
        Stocks = rfDb.Stocks.IgnoreQueryFilters().Count(s => s.IdTwo == rfBusiness),
        Openings = rfDb.StockMovements.IgnoreQueryFilters().Count(m => m.TenantId == rfTenant &&
            m.Reason == Plutus.Catalogue.StockRebuilder.OpeningReason),
        CashEvents = rfDb.CashEvents.IgnoreQueryFilters().Count(c => c.TenantId == rfTenant),
        GiftCards = rfDb.GiftCards.IgnoreQueryFilters().Count(g => g.TenantId == rfTenant),
        GiftCardEntries = rfDb.GiftCardEntries.IgnoreQueryFilters().Count(g => g.TenantId == rfTenant),
        DiscountItems = rfDb.Set<Discount_Item>().IgnoreQueryFilters().Count(d => d.ItemIdTwo == rfBusiness),
        Cics = rfDb.Set<CheckoutItemChange>().IgnoreQueryFilters().Count(c => c.ItemIdTwo == rfBusiness),
    };
    Console.WriteLine($"before: {before.LegacySales} imported + {before.PlatformSales} platform sales · " +
        $"{before.Items} items ({before.Protected} protected) · {before.Stocks} stocks · {before.Openings} openings · " +
        $"{before.CashEvents} cash events · {before.GiftCards}/{before.GiftCardEntries} gift cards/entries");

    // ── 3. Read + plan the reimport (both modes — verify's numbers must BE apply's numbers). ──
    var rfInputs = new Plutus.Migration.Kapow.KapowSalesReader(rfConn).Read(rfTenant, rfStamped.TillId, rfStamped.DeviceId);
    // ⚠ EMPTY known-sets and seq 0: the plan describes the POST-PURGE world in both modes.
    var rfPlan = Plutus.Migration.Kapow.KapowDelta.Plan(rfInputs,
        new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal), 0);
    Console.WriteLine(rfPlan);

    // ⚠ The WP3.4 guardrail, same as sales-v2 — a full replace back-posts EVERY day, so any closed
    // period overlapping ANY of the history must refuse.
    var rfMin = DateOnly.Parse(rfPlan.MinDate![..10]);
    var rfMax = DateOnly.Parse(rfPlan.MaxDate![..10]);
    var rfClosed = rfDb.FinancialPeriods.IgnoreQueryFilters().AsNoTracking()
        .Where(p => p.TenantId == rfTenant && p.Status == Plutus.Entities.Models.PeriodStatus.Closed
                 && p.StartDay <= rfMax && p.EndDay >= rfMin)
        .Select(p => p.Name).ToList();
    if (rfClosed.Count > 0)
    {
        Console.Error.WriteLine($"REFUSED: closed financial period(s) overlap the backup's history: {string.Join(", ", rfClosed)}");
        return 1;
    }

    // Catalogue, read from the backup with the SAME mappings the original loader used (CI dedupe,
    // >20-char ids skipped, Desc coerced non-null — the t1 lesson — and the Image blob carried).
    var rfFileCats = new Dictionary<string, (string Name, string? Desc)>();
    foreach (var r in RfRows("SELECT Id, Name, Description FROM Category"))
        rfFileCats[RfStr(r["Id"])!] = (RfStrE(r["Name"]), RfStr(r["Description"]));

    var rfHaveCats = rfDb.Category.IgnoreQueryFilters().AsNoTracking()
        .Where(c => c.IdTwo == rfBusiness).Select(c => c.IdOne).ToHashSet();
    var rfProtectedKeys = rfDb.Items.IgnoreQueryFilters().AsNoTracking()
        .Where(i => i.IdTwo == rfBusiness && (i.IdOne == rfProtectedGift || EF.Functions.Like(i.IdOne, rfProtectedBagPrefix)))
        .Select(i => i.IdOne).ToList().ToHashSet(StringComparer.OrdinalIgnoreCase);

    var rfItemsByKey = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
    var rfNewCats = new Dictionary<Guid, Category>();
    int rfSkippedLong = 0, rfSkippedDup = 0, rfSkippedProtected = 0;
    foreach (var r in RfRows("SELECT * FROM Items"))
    {
        var id = RfStr(r["Id"]);
        if (id is null || id.Length > 20) { rfSkippedLong++; continue; }
        if (rfItemsByKey.ContainsKey(id)) { rfSkippedDup++; continue; }
        // ⚠ A backup item colliding with a protected key would PK-collide on insert (the protected row
        // survives the purge). None exists today — GIFT-CARD/BAG-% are platform inventions — but a
        // future backup must degrade to a warning, not a crash mid-transaction.
        if (rfProtectedKeys.Contains(id)) { rfSkippedProtected++; continue; }

        var catOld = RfStr(r["CatId"]) ?? "";
        var catId = DetGuid("category", catOld);
        if (!rfHaveCats.Contains(catId) && !rfNewCats.ContainsKey(catId) && rfFileCats.TryGetValue(catOld, out var fc))
            rfNewCats[catId] = new Category { IdOne = catId, IdTwo = rfBusiness, Name = fc.Name, Description = fc.Desc ?? "" };

        rfItemsByKey[id] = new Item
        {
            IdOne = id, IdTwo = rfBusiness,
            Name = RfStrE(r["Name"]), Brand = RfStrE(r["Brand"]), Desc = RfStr(r["Desc"]) ?? "",
            Cost = RfDec(r["Cost"]), ExPrice = RfDec(r["ExPrice"]), Price = RfDec(r["Price"]),
            Image = RfBlob(r["Image"]),
            TaxId = r["VatId"] is null or DBNull ? 0 : Convert.ToInt32(r["VatId"]),
            CatId = catId,
        };
    }
    var rfStockByKey = new Dictionary<string, Stock>(StringComparer.OrdinalIgnoreCase);
    foreach (var r in RfRows("SELECT * FROM Stocks"))
    {
        var id = RfStr(r["ItemId"]);
        if (id is null || !rfItemsByKey.ContainsKey(id)) continue;
        if (rfStockByKey.TryGetValue(id, out var existing)) { existing.Quantity += Convert.ToInt32(r["Quantity"]); continue; }
        rfStockByKey[id] = new Stock { IdOne = id, IdTwo = rfBusiness, IdThree = 1, Quantity = Convert.ToInt32(r["Quantity"]) };
    }
    Console.WriteLine($"backup: {rfItemsByKey.Count} items ({rfSkippedLong} >20-char, {rfSkippedDup} case-dupes, " +
        $"{rfSkippedProtected} protected-key collisions skipped) · {rfNewCats.Count} new categories · {rfStockByKey.Count} stocks");

    if (rfVerify)
    {
        var rfDry = Plutus.Migration.Kapow.KapowMigrator.Migrate(
            rfPlan.ToImport, rfDb, new Plutus.Migration.Kapow.IdRemap<string>(), log: Console.WriteLine,
            write: false, knownQuarantinedRefs: new HashSet<string>(StringComparer.Ordinal));
        Console.WriteLine(rfDry);
        Console.WriteLine();
        Console.WriteLine("VERIFY ONLY — nothing was written. An --apply would:");
        Console.WriteLine($"  DELETE {before.LegacySales} imported sales (their lines/tenders go with them — {before.Lines} lines exist tenant-wide incl. platform), " +
            $"{before.Quarantine} quarantine rows, {before.CashEvents} cash events, " +
            $"{before.GiftCards} gift cards + {before.GiftCardEntries} entries, " +
            $"{before.Items - before.Protected} items (+{before.Stocks} stocks, {before.DiscountItems} discount links, " +
            $"{before.Cics} price-change rows, {before.Openings} stock openings)");
        Console.WriteLine($"  KEEP  {before.PlatformSales} platform sales, {before.Protected} protected items, " +
            "customers/credit, employees/RBAC, tills/devices, themes, GiftCardSettings, PaymentEvents");
        Console.WriteLine($"  IMPORT {rfPlan.ToImport.Count} sales, {rfItemsByKey.Count} items, {rfStockByKey.Count} stocks; " +
            "then reseed stock openings and rebuild rollups");
        return 0;
    }

    // ── 4. APPLY — one transaction, so a failure anywhere leaves the database exactly as dumped. ──
    using (var rfTx = rfDb.Database.BeginTransaction())
    {
        // Purge order is FK order: children of Item first, then Items; sale children, then headers.
        // ⚠ Every delete carries its own tenant/business predicate even though FixedTenantContext
        // filters would scope most of them anyway — the blast radius here is a shared multi-tenant
        // schema, and "the filter would have caught it" is not a defence worth relying on twice.
        var dDiscount = rfDb.Set<Discount_Item>().IgnoreQueryFilters()
            .Where(d => d.ItemIdTwo == rfBusiness).ExecuteDelete();
        var dCics = rfDb.Set<CheckoutItemChange>().IgnoreQueryFilters().Where(c => c.ItemIdTwo == rfBusiness).ExecuteDelete();
        var dStocks = rfDb.Stocks.IgnoreQueryFilters().Where(s => s.IdTwo == rfBusiness).ExecuteDelete();
        var dItems = rfDb.Items.IgnoreQueryFilters().Where(i => i.IdTwo == rfBusiness &&
            i.IdOne != rfProtectedGift && !EF.Functions.Like(i.IdOne, rfProtectedBagPrefix)).ExecuteDelete();

        var dTenders = rfDb.SaleTenders.IgnoreQueryFilters().Where(t => t.TenantId == rfTenant &&
            rfDb.SalesV2.Any(s => s.TenantId == rfTenant && s.LegacyRef != null && s.Id == t.SaleId)).ExecuteDelete();
        var dLines = rfDb.SaleLines.IgnoreQueryFilters().Where(l => l.TenantId == rfTenant &&
            rfDb.SalesV2.Any(s => s.TenantId == rfTenant && s.LegacyRef != null && s.Id == l.SaleId)).ExecuteDelete();
        var dSales = rfDb.SalesV2.IgnoreQueryFilters().Where(s => s.TenantId == rfTenant && s.LegacyRef != null).ExecuteDelete();
        var dQuar = rfDb.SaleQuarantine.IgnoreQueryFilters().Where(q => q.TenantId == rfTenant).ExecuteDelete();

        var dCash = rfDb.CashEvents.IgnoreQueryFilters().Where(c => c.TenantId == rfTenant).ExecuteDelete();
        var dGcEntries = rfDb.GiftCardEntries.IgnoreQueryFilters().Where(g => g.TenantId == rfTenant).ExecuteDelete();
        var dGcCards = rfDb.GiftCards.IgnoreQueryFilters().Where(g => g.TenantId == rfTenant).ExecuteDelete();

        // ⚠ Openings deleted so the reseed below starts from the NEW backup's counts — otherwise
        // SeedOpeningBalancesAsync's per-item "already seeded" check makes the reseed a no-op.
        var dOpen = rfDb.StockMovements.IgnoreQueryFilters().Where(m => m.TenantId == rfTenant &&
            m.Reason == Plutus.Catalogue.StockRebuilder.OpeningReason).ExecuteDelete();

        Console.WriteLine($"purged: {dSales} sales ({dLines} lines, {dTenders} tenders), {dQuar} quarantine, " +
            $"{dItems} items, {dStocks} stocks, {dDiscount} discount links, {dCics} price-change rows, " +
            $"{dOpen} openings, {dCash} cash events, {dGcCards}+{dGcEntries} gift cards+entries");

        foreach (var c in rfNewCats.Values) rfDb.Category.Add(c);
        foreach (var i in rfItemsByKey.Values) rfDb.Items.Add(i);
        foreach (var s in rfStockByKey.Values) rfDb.Stocks.Add(s);
        rfDb.SaveChanges();
        rfDb.ChangeTracker.Clear();   // 20k tracked items would slow every SaveChanges the migrator makes

        var rfRecon = Plutus.Migration.Kapow.KapowMigrator.Migrate(
            rfPlan.ToImport, rfDb, new Plutus.Migration.Kapow.IdRemap<string>(), log: Console.WriteLine,
            write: true, knownQuarantinedRefs: new HashSet<string>(StringComparer.Ordinal));
        Console.WriteLine(rfRecon);

        rfTx.Commit();
    }

    // ── 5. After the commit: the stock reseed, rollups, and the kept-rows invariant. ──
    //
    // ⚠⚠ THE RESEED CANNOT LIVE INSIDE THE TRANSACTION — the t1 rehearsal found this the hard way:
    // `RebuildLevelsAsync` begins its OWN transaction (StockLedger.cs:162), which throws inside an
    // ambient one and rolled the whole apply back. Correct placement, not a workaround: the sales and
    // catalogue are MONEY and stay all-or-nothing above; the ledger seed is IDEMPOTENT — if the
    // process dies on this line, run `stock-open --mysql <conn>` and it completes the same work.
    //
    // The reseed: openings from the new Stocks rows; the heal removes Sale/Return movements older
    // than the reseed (the backup's count is declared the truth at its timestamp — the webstore
    // decrements it never saw are superseded, exactly the §7-item-2 call Matt has now made); the
    // fence stops history replay; levels are rebuilt.
    var rfOpened = await Plutus.Catalogue.StockRebuilder.SeedOpeningBalancesAsync(rfDb, rfTenant);
    Console.WriteLine($"stock openings reseeded: {rfOpened}");
    var (rfSr, rfVr, rfScanned) = await Plutus.Reporting.RollupRebuilder.RebuildAsync(rfDb, rfTenant);
    Console.WriteLine($"rollups rebuilt: {rfSr} SalesRollups + {rfVr} VatRollups from {rfScanned} sales.");

    var after = new { LegacySales = CLegacySales(), PlatformSales = CPlatformSales(), Items = CItems(), Protected = CProtected() };
    Console.WriteLine($"after: {after.LegacySales} imported + {after.PlatformSales} platform sales · " +
        $"{after.Items} items ({after.Protected} protected)");
    if (after.PlatformSales != before.PlatformSales || after.Protected != before.Protected)
    {
        // ⚠ Loud, because this is the invariant the whole design hangs on. The transaction is already
        // committed — this cannot happen from this code's own deletes (they exclude these rows by
        // predicate), so if it fires, something else wrote concurrently and a human must reconcile.
        Console.Error.WriteLine("⚠⚠ INVARIANT BROKEN: platform sales or protected items changed count. INVESTIGATE.");
        return 1;
    }

    var penny = rfDb.SalesV2.IgnoreQueryFilters().Where(s => s.TenantId == rfTenant)
        .GroupBy(_ => 1).Select(g => new { Gross = g.Sum(s => s.GrossPence), Vat = g.Sum(s => s.VatPence), N = g.Count() }).Single();
    var lineSum = rfDb.SaleLines.IgnoreQueryFilters().Where(l => l.TenantId == rfTenant).Sum(l => l.LineGrossPence);
    var rollGross = rfDb.SalesRollups.IgnoreQueryFilters().Where(r => r.TenantId == rfTenant).Sum(r => r.GrossPence);
    var vatRoll = rfDb.VatRollups.IgnoreQueryFilters().Where(r => r.TenantId == rfTenant).Sum(r => r.VatPence);
    Console.WriteLine($"four-way: headers £{penny.Gross / 100m:0.00} ({penny.N} sales) | Σlines £{lineSum / 100m:0.00} | " +
        $"rollups £{rollGross / 100m:0.00} | VAT £{penny.Vat / 100m:0.00} vs VatRollups £{vatRoll / 100m:0.00}");
    Console.WriteLine(penny.Gross == lineSum && penny.Gross == rollGross && penny.Vat == vatRoll
        ? "four-way penny check: EQUAL ✔" : "⚠⚠ FOUR-WAY PENNY CHECK FAILED — do not walk away from this.");
    return 0;
}

// Deterministic ids so re-runs are idempotent and cross-references stable.
static Guid DetGuid(string kind, string key)
{
    var hash = MD5.HashData(Encoding.UTF8.GetBytes($"plutus-seed:{kind}:{key}"));
    return new Guid(hash);
}
var businessId = DetGuid("business", "kapow");
var tillId = DetGuid("till", "kapow-till-1");
const int StoreIdNew = 1;

static decimal Dec(object v) => v is null or DBNull ? 0m : decimal.Parse(Convert.ToString(v), CultureInfo.InvariantCulture);
static double Dbl(object v) => v is null or DBNull ? 0d : Convert.ToDouble(v, CultureInfo.InvariantCulture);
static int Int(object v) => v is null or DBNull ? 0 : Convert.ToInt32(v);
static int? IntN(object v) => v is null or DBNull ? null : Convert.ToInt32(v);
static bool Bool(object v) => v is not (null or DBNull) && Convert.ToInt64(v) != 0;
static string Str(object v) => v is null or DBNull ? null : Convert.ToString(v);
// For [Required] fields: RequiredAttribute rejects empty strings, and the old data
// has plenty (ContactNumber, Brand, …) — coerce to "-" so validation passes.
static string StrE(object v) { var s = Str(v); return string.IsNullOrWhiteSpace(s) ? "-" : s; }
// For optional strings whose DB columns are still NOT NULL: null → "".
static string StrO(object v) => Str(v) ?? string.Empty;
static Guid Gd(object v) => Guid.Parse(Convert.ToString(v));
static byte[] Blob(object v) => v is null or DBNull ? null : (byte[])v;
static DateTime Dt(object v) => v is null or DBNull
    ? DateTime.UnixEpoch
    : DateTime.Parse(Convert.ToString(v), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

using var src = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = oldDbPath, Mode = SqliteOpenMode.ReadOnly }.ToString());
src.Open();

List<Dictionary<string, object>> Rows(string sql)
{
    var list = new List<Dictionary<string, object>>();
    using var cmd = src.CreateCommand();
    cmd.CommandText = sql;
    using var r = cmd.ExecuteReader();
    while (r.Read())
    {
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.GetValue(i);
        list.Add(row);
    }
    return list;
}

void Audit(Auditable e, Dictionary<string, object> row)
{
    e.CreatedAt = Dt(row.GetValueOrDefault("Created"));
    e.ModifiedAt = Dt(row.GetValueOrDefault("Modified"));
    // QueryParameters.GetExpression filters ModifiedAt >= UnixEpoch — rows with a
    // pre-epoch ModifiedAt silently vanish from every Index endpoint.
    if (e.ModifiedAt < e.CreatedAt) e.ModifiedAt = e.CreatedAt;
    e.CreatedBy = Str(row.GetValueOrDefault("CreatedBy")) ?? SeedUser;
    e.ModifiedBy = Str(row.GetValueOrDefault("ModifiedBy")) ?? SeedUser;
}

// ── extract + transform ──────────────────────────────────────────────────────
Console.WriteLine($"Reading {Path.GetFileName(oldDbPath)} …");

var oldStore = Rows("SELECT * FROM Stores").Single();

var business = new Business
{
    Id = businessId,
    Name = StrE(oldStore["StoreName"]),
    NameAbbr = Str(oldStore["StoreAbbr"]) ?? "KAPOW",
    VatIN = StrE(oldStore["VatIN"]),
    RecMarkup = oldStore["RecMarkup"] is null or DBNull ? null : Dec(oldStore["RecMarkup"]),
    Logo = Blob(oldStore["Logo"]),
    CreatedAt = Dt(oldStore["Created"]), ModifiedAt = Dt(oldStore["Modified"]),
    CreatedBy = SeedUser, ModifiedBy = SeedUser,
};

var store = new Store
{
    Id = StoreIdNew,
    BusinessId = businessId,
    AdLine1 = StrE(oldStore["AdLine1"]), AdLine2 = StrO(oldStore["AdLine2"]),
    City = StrE(oldStore["City"]), PostCode = StrE(oldStore["PostCode"]),
    Country = StrE(oldStore["Country"]), FullAddress = StrO(oldStore["FullAddress"]),
    ContactNumber = StrE(oldStore["ContactNumber"]),
    CreatedBy = SeedUser, ModifiedBy = SeedUser,
};

var till = new Till
{
    Id = tillId, StoreId = StoreIdNew, CashFloat = 0m, LastOnline = DateTime.UtcNow,
    CreatedBy = SeedUser, ModifiedBy = SeedUser,
};

var taxes = Rows("SELECT * FROM Vats").Select(r =>
{
    var t = new Tax { IdOne = Int(r["Id"]), IdTwo = businessId, Name = StrE(r["Name"]), Rate = Dbl(r["Rate"]) };
    Audit(t, r); return t;
}).ToList();

// Old Category ids are ints; new key is (Guid, BusinessId) — deterministic Guid per old id.
Guid CatGuid(object oldId) => DetGuid("category", Convert.ToString(oldId));
var categories = Rows("SELECT * FROM Category").Select(r =>
{
    var c = new Category { IdOne = CatGuid(r["Id"]), IdTwo = businessId, Name = StrE(r["Name"]), Description = StrO(r["Description"]) };
    Audit(c, r); return c;
}).ToList();

var employees = Rows("SELECT * FROM Employees").Select(r =>
{
    var e = new Employee
    {
        Id = Gd(r["Id"]), BusinessId = businessId, StoreId = StoreIdNew,
        FName = StrE(r["FName"]), LName = StrE(r["LName"]),
        Email = StrE(r["Email"]), Mobile = StrE(r["Mobile"]),
        AdLine1 = StrE(r["AdLine1"]), AdLine2 = StrO(r["AdLine2"]), City = StrE(r["City"]),
        PostCode = StrE(r["PostCode"]), Country = StrE(r["Country"]), FullAddress = StrO(r["FullAddress"]),
        Wage = Dec(r["Wage"]), ContractedHours = Int(r["ContractedHours"]),
        NIN = StrE(r["NIN"]), Active = Bool(r["Active"]),
    };
    Audit(e, r); return e;
}).ToList();

var authActions = Rows("SELECT * FROM AuthActions").Select(r =>
{
    var a = new AuthActions { Id = Int(r["Id"]), Name = StrE(r["Name"]), Amount = Dec(r["Amount"]), Module = "POS" };
    Audit(a, r); return a;
}).ToList();

var empAuths = Rows("SELECT * FROM EmpAuthActions").Select(r =>
{
    var ea = new Emp_AuthActions { AuthAId = Int(r["AuthAId"]), EmpId = Gd(r["EmpId"]), Permissions = (Plutus.Entities.Enums.Permissions)Int(r["Permissions"]) };
    Audit(ea, r); return ea;
}).ToList();

var payMethods = Rows("SELECT * FROM PayMethods").Select(r =>
{
    var p = new PaymentMethod
    {
        Id = Int(r["Id"]), Name = StrE(r["Name"]), Charge = Dec(r["Charge"]),
        MinimumCharge = Dec(r["MinimumCharge"]), IsChangeable = Bool(r["IsChangeable"]), IsCashBackable = Bool(r["IsCashBackable"]),
    };
    Audit(p, r); return p;
}).ToList();

var discounts = Rows("SELECT * FROM Discounts").Select(r =>
{
    var d = new Discount
    {
        Id = Int(r["Id"]), BusinessId = businessId, Name = StrE(r["Name"]),
        AllApplicable = Bool(r["AllApplicable"]), CanUseWithOtherDiscounts = Bool(r["CanUseWithOtherDiscounts"]),
        AutoApply = Bool(r["AutoApply"]), Type = Int(r["Type"]), Amount = Dec(r["Amount"]),
        UsesPerTransaction = Int(r["UsesPerTransaction"]), RequiredNumOfItems = Int(r["RequiredNumOfItems"]),
    };
    Audit(d, r); return d;
}).ToList();

// Discount_Category's discount FK is a shadow property (navigation only) — carry the
// old id alongside and set it via the change tracker at load time.
var discountCats = Rows("SELECT * FROM DiscountCats").Select(r =>
{
    var dc = new Discount_Category
    {
        Id = Int(r["Id"]),
        CatIdOne = CatGuid(r["CatId"]), CatIdTwo = businessId,
        StartDateTime = Dt(r["StartDateTime"]), EndDateTime = Dt(r["EndDateTime"]),
    };
    Audit(dc, r); return (entity: dc, discountId: Int(r["DiscountId"]));
}).ToList();

// Item.IdOne is MaxLength(20) — old ids longer than that can't load; they (and their
// dependent rows) are skipped with a warning rather than silently truncated.
var longIds = new HashSet<string>();
var dupIds = new List<string>();
// MySQL's utf8mb4_0900_ai_ci PK collation is case/accent-insensitive; old SQLite ids
// differing only by case ("Poster" vs "poster") collide — keep the first, log the rest.
var itemsByKey = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
foreach (var r in Rows("SELECT * FROM Items"))
{
    var id = Str(r["Id"]);
    if (id.Length > 20) { longIds.Add(id); continue; }
    if (itemsByKey.ContainsKey(id)) { dupIds.Add(id); continue; }
    var i = new Item
    {
        IdOne = id, IdTwo = businessId,
        Name = StrE(r["Name"]), Brand = StrE(r["Brand"]), Desc = StrO(r["Desc"]),
        Cost = Dec(r["Cost"]), ExPrice = Dec(r["ExPrice"]), Price = Dec(r["Price"]),
        Image = Blob(r["Image"]), TaxId = Int(r["VatId"]), CatId = CatGuid(r["CatId"]),
    };
    Audit(i, r); itemsByKey[id] = i;
}
var items = itemsByKey.Values.ToList();
bool KnownItem(object idV) { var s = Str(idV); return s != null && itemsByKey.ContainsKey(s); }

// Same CI-collation dedupe for stocks — quantities of case-variant ids are summed.
var stockByKey = new Dictionary<string, Stock>(StringComparer.OrdinalIgnoreCase);
foreach (var r in Rows("SELECT * FROM Stocks").Where(r => KnownItem(r["ItemId"])))
{
    var id = Str(r["ItemId"]);
    if (stockByKey.TryGetValue(id, out var existing)) { existing.Quantity += Int(r["Quantity"]); continue; }
    var s = new Stock { IdOne = id, IdTwo = businessId, IdThree = StoreIdNew, Quantity = Int(r["Quantity"]) };
    Audit(s, r); stockByKey[id] = s;
}
var stocks = stockByKey.Values.ToList();

var checkoutChanges = Rows("SELECT * FROM CheckoutItemChangeModel").Where(r => KnownItem(r["ItemId"])).Select(r =>
{
    var c = new CheckoutItemChange { Id = Int(r["Id"]), Price = Dec(r["Price"]), ExPrice = Dec(r["ExPrice"]), ItemIdOne = Str(r["ItemId"]), ItemIdTwo = businessId };
    Audit(c, r); return c;
}).ToList();
var knownCic = checkoutChanges.Select(c => c.Id).ToHashSet();

// Old Sale ids are NatApp timestamp strings ("201912310816239"), not GUIDs —
// map each to a deterministic GUID and translate every referencing table.
var saleGuid = new Dictionary<string, Guid>();
Guid SaleGuid(object oldId)
{
    var key = Convert.ToString(oldId);
    if (!saleGuid.TryGetValue(key, out var g)) saleGuid[key] = g = DetGuid("sale", key);
    return g;
}
var sales = Rows("SELECT * FROM Sales").Select(r =>
{
    var s = new Sale
    {
        Id = SaleGuid(r["Id"]), Total = Dec(r["Total"]), TotalExTax = Dec(r["TotalExTax"]),
        DateOfSale = Dt(r["DateOfSale"]), EmployeeId = Gd(r["EmployeeId"]), StoreId = StoreIdNew, TillId = tillId,
    };
    Audit(s, r); return s;
}).ToList();
bool KnownSale(object oldId) => saleGuid.ContainsKey(Convert.ToString(oldId) ?? "");

var transSaleLookup = new Dictionary<int, Guid>();
var transactions = new List<Transaction>();
foreach (var r in Rows("SELECT * FROM Trans"))
{
    if (!KnownItem(r["ItemId"]) || !KnownSale(r["SaleId"])) continue;
    var saleId = SaleGuid(r["SaleId"]);
    var cicId = IntN(r["CheckoutItemChangeId"]);
    var t = new Transaction
    {
        IdOne = Int(r["Id"]), IdTwo = saleId, Amount = Int(r["Amount"]),
        ItemCostExPrice = Dec(r["ItemCostExPrice"]), ItemCostPrice = Dec(r["ItemCostPrice"]),
        ItemIdOne = Str(r["ItemId"]), ItemIdTwo = businessId, TillId = tillId,
        CheckoutItemChangeId = cicId.HasValue && knownCic.Contains(cicId.Value) ? cicId : null,
    };
    Audit(t, r);
    transactions.Add(t);
    transSaleLookup[t.IdOne] = saleId;
}

// New Note key is (NoteId, SaleId) — fold the old NotesSales join into the Note itself.
var noteText = Rows("SELECT * FROM Notes").ToDictionary(r => Int(r["Id"]), r => r);
var notes = Rows("SELECT * FROM NotesSales").Where(r => KnownSale(r["SaleId"]) && noteText.ContainsKey(Int(r["NoteId"]))).Select(r =>
{
    var srcNote = noteText[Int(r["NoteId"])];
    var n = new Note { IdOne = Int(r["NoteId"]), IdTwo = SaleGuid(r["SaleId"]), Text = StrE(srcNote["Note"]) };
    Audit(n, srcNote); return n;
}).ToList();

var paySales = Rows("SELECT * FROM PaySales").Where(r => KnownSale(r["SaleId"])).Select(r =>
{
    var p = new PaymentMethod_Sale { PayId = Int(r["PayId"]), SaleId = SaleGuid(r["SaleId"]), Amount = Dec(r["Amount"]), Change = Dec(r["Change"]) };
    Audit(p, r); return p;
}).ToList();

// Old key was (TransactionId, DiscountId); new key needs SaleId — derived via Trans.
var transDiscounts = Rows("SELECT * FROM Transaction_Discounts").Where(r => transSaleLookup.ContainsKey(Int(r["TransactionId"]))).Select(r =>
{
    var td = new Transaction_Discount
    {
        TransactionId = Int(r["TransactionId"]), SaleId = transSaleLookup[Int(r["TransactionId"])],
        DiscountId = Int(r["DiscountId"]), DiscountRate = Dec(r["DiscountRate"]),
    };
    Audit(td, r); return td;
}).ToList();

// Old refunds can have an empty AuthoriserId — fall back to the (sole) employee.
var fallbackAuthoriser = employees[0].Id;
var refunds = Rows("SELECT * FROM Refunds").Where(r => KnownItem(r["ItemId"]) && KnownSale(r["SaleId"]) && KnownSale(r["SaleIdReturned"])).Select(r =>
{
    var cicId = IntN(r["CheckoutItemChangeId"]);
    var authoriser = Str(r["AuthoriserId"]);
    var rf = new Refund
    {
        Id = Int(r["Id"]), Reason = StrE(r["Reason"]), Amount = Int(r["Amount"]),
        ItemIdOne = Str(r["ItemId"]), ItemIdTwo = businessId,
        SaleId = SaleGuid(r["SaleId"]), SaleIdReturned = SaleGuid(r["SaleIdReturned"]),
        AuthoriserId = string.IsNullOrWhiteSpace(authoriser) ? fallbackAuthoriser : Guid.Parse(authoriser),
        CheckoutItemChangeId = cicId.HasValue && knownCic.Contains(cicId.Value) ? cicId : null,
    };
    Audit(rf, r); return rf;
}).ToList();

// ── report ───────────────────────────────────────────────────────────────────
Console.WriteLine($"""
    Mapped:
      Business            1   (from Stores: "{business.Name}")
      Store               1   Till 1 (synthesised: {tillId})
      Taxes               {taxes.Count}
      Categories          {categories.Count}
      Employees           {employees.Count}   (credentials NOT ported — by design)
      AuthActions         {authActions.Count} / EmpAuths {empAuths.Count}
      PaymentMethods      {payMethods.Count}
      Discounts           {discounts.Count} (+{discountCats.Count} category links)

      Items               {items.Count}   (skipped {longIds.Count} long ids, {dupIds.Count} case-duplicate ids)
      Stocks              {stocks.Count}
      CheckoutItemChanges {checkoutChanges.Count}
      Sales               {sales.Count}
      Transactions        {transactions.Count}
      Notes               {notes.Count}
      PaySales            {paySales.Count}
      TransactionDiscounts {transDiscounts.Count}
      Refunds             {refunds.Count}
    """);
if (longIds.Count > 0)
    Console.WriteLine($"  WARNING skipped item ids: {string.Join(", ", longIds.Take(10))}{(longIds.Count > 10 ? " …" : "")}");

if (dryRun) { Console.WriteLine("Dry run — nothing written."); return 0; }

// ── load into MySQL ──────────────────────────────────────────────────────────
Console.WriteLine("Connecting to MySQL …");
using var ctx = new MySqlDbContext(mysqlConn);
ctx.CurrentUser = SeedUser;
ctx.Database.Migrate(); // creates the new schema from the checked-in migrations
ctx.SetSyncState(true); // preserve the original audit values from the backup
ctx.ChangeTracker.AutoDetectChangesEnabled = false;

async Task Load<T>(string name, IEnumerable<T> entities, int batch = 2000) where T : class
{
    var n = 0;
    foreach (var chunk in entities.Chunk(batch))
    {
        ctx.Set<T>().AddRange(chunk);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        n += chunk.Length;
        Console.Write($"\r  {name}: {n}      ");
    }
    Console.WriteLine();
}

await Load("Business", new[] { business });
await Load("Stores", new[] { store });
await Load("Tills", new[] { till });
await Load("Taxes", taxes);
await Load("Categories", categories);
await Load("Employees", employees);
await Load("AuthActions", authActions);
await Load("EmpAuths", empAuths);
await Load("PaymentMethods", payMethods);
await Load("Discounts", discounts);
foreach (var (entity, discountId) in discountCats)
{
    ctx.Set<Discount_Category>().Add(entity);
    ctx.Entry(entity).Property("DiscountId").CurrentValue = discountId;
}
await ctx.SaveChangesAsync();
ctx.ChangeTracker.Clear();
Console.WriteLine($"  DiscountCats: {discountCats.Count}");
await Load("Items", items, 500); // image blobs — smaller batches
await Load("Stocks", stocks);
await Load("CheckoutItemChanges", checkoutChanges);
await Load("Sales", sales);

// Transaction.Sale is a [Required] NAVIGATION — validation demands a non-null object.
// Attach per-batch Unchanged stubs so validation passes without re-inserting Sales.
{
    var n = 0;
    foreach (var chunk in transactions.Chunk(2000))
    {
        var stubs = new Dictionary<Guid, Sale>();
        foreach (var t in chunk)
        {
            if (!stubs.TryGetValue(t.IdTwo, out var s))
            {
                s = new Sale { Id = t.IdTwo };
                ctx.Attach(s).State = EntityState.Unchanged;
                stubs[t.IdTwo] = s;
            }
            t.Sale = s;
        }
        ctx.Set<Transaction>().AddRange(chunk);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        n += chunk.Length;
        Console.Write($"\r  Transactions: {n}      ");
    }
    Console.WriteLine();
}
await Load("Notes", notes);
await Load("PaySales", paySales);
await Load("TransactionDiscounts", transDiscounts);
await Load("Refunds", refunds);

Console.WriteLine("Done.");
return 0;
