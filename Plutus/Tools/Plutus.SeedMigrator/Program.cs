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

var oldDbPath = args[0];
var dryRun = args[1] == "--dry-run";
var mysqlConn = dryRun ? null : args[2 == args.Length ? 1 : 2];
if (!dryRun && args[1] == "--mysql") mysqlConn = args[2];
if (!File.Exists(oldDbPath)) { Console.Error.WriteLine($"not found: {oldDbPath}"); return 1; }

// ── T1.8 thin runner: Kapow → sales-v2. Reuses the Plutus.Migration.Kapow library. ──
//   Plutus.SeedMigrator <kapow.db> sales-v2 --sqlite <out.db>      (validation run)
//   Plutus.SeedMigrator <kapow.db> sales-v2 --mysql "<connstring>" (cutover)
if (Array.IndexOf(args, "sales-v2") >= 0)
{
    var tenantId = Plutus.Entities.Tenancy.KnownTenants.Kapow;
    using var kapowConn = new SqliteConnection(
        new SqliteConnectionStringBuilder { DataSource = oldDbPath, Mode = SqliteOpenMode.ReadOnly }.ToString());
    kapowConn.Open();
    var inputs = new Plutus.Migration.Kapow.KapowSalesReader(kapowConn)
        .Read(tenantId, tillId: Guid.NewGuid(), deviceId: Guid.NewGuid());
    Console.WriteLine($"read {inputs.Count} Kapow sales; mapping…");

    var tenantCtx = new Plutus.Entities.Tenancy.FixedTenantContext(tenantId);
    var sqliteIdx = Array.IndexOf(args, "--sqlite");
    var mysqlIdx = Array.IndexOf(args, "--mysql");
    var options = new DbContextOptionsBuilder<MySqlDbContext>();
    if (sqliteIdx >= 0 && sqliteIdx + 1 < args.Length)
    {
        var outPath = args[sqliteIdx + 1];
        if (File.Exists(outPath)) File.Delete(outPath);
        options.UseSqlite($"Data Source={outPath}");
    }
    else if (mysqlIdx >= 0 && mysqlIdx + 1 < args.Length)
        options.UseMySql(args[mysqlIdx + 1], MySqlServerVersion.LatestSupportedServerVersion);
    else { Console.Error.WriteLine("sales-v2 needs --sqlite <out.db> or --mysql <conn>"); return 1; }

    using var target = new MySqlDbContext(options.Options, tenantCtx);
    target.Database.EnsureCreated();
    var recon = Plutus.Migration.Kapow.KapowMigrator.Migrate(
        inputs, target, new Plutus.Migration.Kapow.IdRemap<string>(), log: Console.WriteLine);
    Console.WriteLine();
    Console.WriteLine(recon);
    if (recon.QuarantineReasons.Count > 0)
    {
        Console.WriteLine("sample quarantine reasons:");
        foreach (var q in recon.QuarantineReasons) Console.WriteLine("  - " + q);
    }
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
