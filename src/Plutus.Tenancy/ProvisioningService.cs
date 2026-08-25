using System;
using System.Threading.Tasks;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Tenancy
{
    /// <summary>
    /// ⚠ `IsSandbox` ADDED TO PROVISIONING 2026-08-23. Six places exclude sandbox tenants from the
    /// commercial rollups — MRR, analytics, usage, contracts — so a tenant created to TEST against and
    /// not flagged is counted as a paying subscriber and inflates the revenue figure.
    ///
    /// ⚠ A SETTER ALREADY EXISTED and this does not replace it: `SandboxController` serves
    /// `PUT /api/v1/platform/tenants/{id}/sandbox`, and the portal has a toggle on the subscriber detail.
    /// What was missing is setting it AT CREATION — between provisioning and remembering to flip the
    /// toggle, a test tenant counts as real, and nothing prompts anyone to flip it.
    ///
    /// ⚠ It defaults to FALSE here. A real subscriber is the common case for the API, and a flag that
    /// defaults to "not real" is one nobody notices is wrong until money is missing from a report.
    /// ⚠ The PORTAL dialog defaults it to TRUE, deliberately — see NewTenantDialog.
    /// </summary>
    public sealed record ProvisionRequest(string Name, string Plan, string AdminEmail, string AdminPassword, bool IsSandbox = false);
    public sealed record ProvisionResult(Guid TenantId, Guid CompanyId, int StoreId, Guid AdminUserId);

    /// <summary>
    /// T1.2 tenant provisioning: creates a Tenant plus its default Company (Business), default
    /// Store ("Main"), an admin user (Employee + WebCredential login) — and, since 2026-08-23, the
    /// tenant's built-in ROLES with that admin assigned **Owner**. Runs as platform-admin, so
    /// TenantId is stamped EXPLICITLY on the tenant-owned rows (the ambient context is unscoped and
    /// does not auto-stamp).
    ///
    /// ⚠⚠ THE ROLE STEP WAS MISSING UNTIL 2026-08-23, AND THAT MADE EVERY TENANT THIS CREATED
    /// UNUSABLE. Without an assignment `ResolveLoginScopesAsync` falls to its legacy branch and
    /// hands the admin `pos.sell` and nothing else — so the person the shop was created for could
    /// sign in and then not create a till, add a user, or manage the company. **`Demo Store`, with
    /// its 0 stores / 0 tills / 0 employees, is what this endpoint used to produce.**
    /// </summary>
    public sealed class ProvisioningService
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantRoleProvisioner _roles;

        /// <summary>
        /// ⚠⚠ `roles` IS REQUIRED, DELIBERATELY. It was tempting to make it optional so existing
        /// call sites kept compiling — but "provisioning silently produces a tenant nobody can
        /// configure" is the exact bug being fixed, and an optional dependency is how it would come
        /// back. A host that registers Tenancy without Identity now fails loudly at construction.
        /// </summary>
        public ProvisioningService(MySqlDbContext db, ITenantRoleProvisioner roles)
        {
            _db = db;
            _roles = roles ?? throw new ArgumentNullException(nameof(roles),
                "Provisioning must be able to seed roles, or it creates tenants nobody can configure.");
        }

        public async Task<ProvisionResult> ProvisionAsync(ProvisionRequest req, string actingUser, System.Threading.CancellationToken ct = default)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Name) ||
                string.IsNullOrWhiteSpace(req.AdminEmail) || string.IsNullOrWhiteSpace(req.AdminPassword))
                throw new EnrolmentException(400, "name, adminEmail and adminPassword are required.");

            _db.CurrentUser = actingUser;
            var tenantId = Uuid7.New();

            await using var tx = await _db.Database.BeginTransactionAsync();

            _db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = req.Name,
                Status = 1, // Active
                Plan = string.IsNullOrWhiteSpace(req.Plan) ? "standard" : req.Plan,
                Entitlements = "[]",
                IsSandbox = req.IsSandbox,
                ConnectionRef = "",
                CreatedAtUtc = DateTime.UtcNow,
            });

            var business = new Business
            {
                Id = Uuid7.New(),
                Name = req.Name,
                NameAbbr = Abbr(req.Name),
                VatIN = "",
            };
            _db.Business.Add(business);
            _db.Entry(business).Property("TenantId").CurrentValue = tenantId;

            var store = new Store
            {
                BusinessId = business.Id,
                // [Required] fields cannot be empty; seed placeholders the tenant edits later.
                ContactNumber = "N/A",
                PostCode = "N/A",
                AdLine1 = "Main",
                AdLine2 = "",
                City = "",
                Country = "",
            };
            _db.Stores.Add(store);
            _db.Entry(store).Property("TenantId").CurrentValue = tenantId;

            await _db.SaveChangesAsync(); // assigns store.Id (identity)

            // ⚠⚠ THE FIRST STORE GETS A NAME — added 2026-08-24, and its absence cost Matt a
            // support round trip. Provisioning created a Store row and no `StoreDetails`, so the
            // store had NO NAME: `StoresController.List` returns `name: null` for it and the portal
            // shows a nameless row. He read that as "no store yet", created "Test Store", and the
            // retry hit a perfectly correct duplicate-name conflict he had no way to explain.
            //
            // ⚠ Named after the business rather than "Main". A single-store shop is the common case
            // and its store IS the business; "Main" is an address placeholder, not a name, and
            // putting it in the name column would just move the confusion.
            //
            // ⚠ It is a real name and therefore takes part in the per-tenant uniqueness check, which
            // is correct: a second store called the same thing as the business should collide.
            _db.StoreDetails.Add(new StoreDetails
            {
                StoreId = store.Id,
                TenantId = tenantId,
                Name = req.Name.Trim(),
            });

            SeedCatalogueFoundations(tenantId, business.Id);

            var admin = new Employee
            {
                Id = Uuid7.New(),
                Email = req.AdminEmail.Trim(),
                FName = "Admin",
                LName = req.Name,
                Mobile = "N/A",   // [Required]; tenant edits later
                PostCode = "N/A", // [Required] via Address base
                AdLine1 = "",     // NOT NULL columns (People)
                AdLine2 = "",
                City = "",
                Country = "",
                NIN = "",
                Active = true,
                BusinessId = business.Id,
                StoreId = store.Id,
            };
            _db.Employees.Add(admin);
            _db.Entry(admin).Property("TenantId").CurrentValue = tenantId;

            var (hash, salt) = Pbkdf2.Hash(req.AdminPassword);
            _db.WebCredentials.Add(new WebCredential
            {
                Email = req.AdminEmail.Trim(),
                EmployeeId = admin.Id,
                HashedPassword = Convert.ToBase64String(hash),
                Salt = Convert.ToBase64String(salt),
            });

            await _db.SaveChangesAsync();

            // ⚠⚠ INSIDE THE TRANSACTION, AND THAT IS THE WHOLE POINT. A tenant that commits with an
            // admin but no Owner role is precisely the half-built state this call was added to
            // prevent — it would look provisioned, sign in, and be unable to do anything. Either
            // both land or neither does.
            await _roles.EnsureRolesAndOwnerAsync(tenantId, business.Id, admin.Id);

            await tx.CommitAsync();

            return new ProvisionResult(tenantId, business.Id, store.Id, admin.Id);
        }

        /// <summary>
        /// The VAT bands, rate points and categories a UK shop cannot trade without.
        ///
        /// ⚠⚠ ADDED 2026-08-25, AND ITS ABSENCE MADE EVERY PROVISIONED TENANT UNABLE TO PRICE
        /// ANYTHING. Matt: *"Everything going forward needs to be current and up to date, not
        /// needing backfills."* Checked against the live database that evening:
        ///
        ///     tenant           taxes  categories  items
        ///     Kapow                3          12  20508
        ///     Test Business        0           0      0
        ///     Demo Store           0           0      0
        ///
        /// An item needs a `TaxId`, so with no bands a tenant could not create a single product —
        /// and the start-up sweep that provisions the gift-card and card-surcharge rows prices them
        /// against a band, so it silently skipped both tenants through five reboots. **Nothing
        /// errored. It just quietly was not a shop.**
        ///
        /// ⚠ INSIDE THE CALLER'S TRANSACTION, like the roles: a tenant that commits half-equipped is
        /// the state this whole method exists to prevent.
        ///
        /// ⚠⚠ THE DEFAULT BANDS ARE A STATED ASSUMPTION, NOT A DERIVED FACT — UK rates as at
        /// 2026-08-25: 20% standard, 5% reduced, 0% zero-rated. A shop that needs different ones
        /// edits them in the portal. ⚠ **Zero-rated and EXEMPT stay separate** and no exempt band is
        /// seeded, deliberately: they are both 0% to a customer and different in law, and a tenant
        /// that has one by default would have people filing under it without meaning to. Kapow sells
        /// no exempt goods; other tenants will, and they add it when they do.
        ///
        /// ⚠ `Tax.Rate` is a MULTIPLIER (1.2 for 20%), not a percentage — the legacy shape, and an
        /// easy and expensive thing to get wrong. `VatRatePoint.RateBp` is basis points (2000 = 20%).
        /// </summary>
        private void SeedCatalogueFoundations(Guid tenantId, Guid companyId)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            // (band key, display, class, basis points, legacy multiplier)
            var bands = new (string Band, string Display, VatClass Class, int RateBp, double Multiplier)[]
            {
                ("standard", "20%",                     VatClass.Standard, 2000, 1.20),
                ("reduced",  "5%",                      VatClass.Reduced,   500, 1.05),
                ("zero",     "Zero rated (books & printed matter)", VatClass.Zero, 0, 1.00),
            };

            var taxId = 0;
            foreach (var b in bands)
            {
                // The legacy band an Item's TaxId points at.
                // ⚠ `IdOne` IS ASSIGNED BY HAND. `Tax` is `CompositeBase<int, Guid>` — a COMPOSITE
                // key of (IdOne, IdTwo) — and a database cannot auto-generate one column of a
                // composite key, so leaving it default fails with
                // `NOT NULL constraint failed: Taxes.IdOne`. Kapow's are 1, 2, 3; because IdTwo is
                // the company, each tenant numbers its own bands from 1 without colliding.
                var tax = new Tax { IdOne = ++taxId, IdTwo = companyId, Name = b.Display, Rate = b.Multiplier };
                _db.Taxes.Add(tax);
                _db.Entry(tax).Property("TenantId").CurrentValue = tenantId;

                // The published band the tills and the VAT return read.
                _db.VatRatePoints.Add(new VatRatePoint
                {
                    Id = Uuid7.New(),
                    TenantId = tenantId,
                    Band = b.Band,
                    DisplayName = b.Display,
                    Class = (int)b.Class,
                    RateBp = b.RateBp,
                    // ⚠ The epoch, not "now": a band effective from the moment of provisioning would
                    // leave any sale timestamped a second earlier with no rate to resolve against.
                    EffectiveFromUtc = epoch,
                    Note = "Seeded at provisioning, 2026-08-25 UK defaults. Edit in the portal.",
                });
            }

            // ⚠ Categories are REQUIRED — `Item.CatId` is not nullable, and the category delete
            // refuses to remove the last one. A tenant with none cannot create a product at all.
            // ⚠ These two names match what the gift-card and surcharge provisioners look for, so
            // those rows land in the right place rather than in a stray "General".
            foreach (var (name, desc) in new[]
            {
                ("General", "The starting category. Rename it, or add your own in Inventory → Categories."),
                ("Gift cards", "Gift cards and vouchers — money taken as a liability, not product sales."),
                ("Payment fees", "Card surcharges and payment fees — not product sales."),
            })
            {
                var cat = new Category { IdOne = Uuid7.New(), IdTwo = companyId, Name = name, Description = desc };
                _db.Category.Add(cat);
                _db.Entry(cat).Property("TenantId").CurrentValue = tenantId;
            }
        }

        private static string Abbr(string name)
        {
            var trimmed = (name ?? "").Trim();
            return trimmed.Length <= 5 ? trimmed.ToUpperInvariant() : trimmed[..5].ToUpperInvariant();
        }
    }
}
