using System;
using System.Threading.Tasks;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Tenancy
{
    public sealed record ProvisionRequest(string Name, string Plan, string AdminEmail, string AdminPassword);
    public sealed record ProvisionResult(Guid TenantId, Guid CompanyId, int StoreId, Guid AdminUserId);

    /// <summary>
    /// T1.2 tenant provisioning: creates a Tenant plus its default Company (Business), default
    /// Store ("Main") and an admin user (Employee + WebCredential login). Runs as platform-admin,
    /// so TenantId is stamped EXPLICITLY on the tenant-owned rows (the ambient context is
    /// unscoped and does not auto-stamp).
    /// </summary>
    public sealed class ProvisioningService
    {
        private readonly MySqlDbContext _db;
        public ProvisioningService(MySqlDbContext db) => _db = db;

        public async Task<ProvisionResult> ProvisionAsync(ProvisionRequest req, string actingUser)
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
            await tx.CommitAsync();

            return new ProvisionResult(tenantId, business.Id, store.Id, admin.Id);
        }

        private static string Abbr(string name)
        {
            var trimmed = (name ?? "").Trim();
            return trimmed.Length <= 5 ? trimmed.ToUpperInvariant() : trimmed[..5].ToUpperInvariant();
        }
    }
}
