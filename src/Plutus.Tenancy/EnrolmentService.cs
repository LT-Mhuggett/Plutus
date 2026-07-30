using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Tenancy
{
    // ---- results (plaintext code/secret returned ONCE, never persisted) ----
    public sealed record CreateTillResult(Guid TillId, string EnrolmentCode, DateTime ExpiresAtUtc);
    public sealed record EnrolResult(Guid DeviceId, string ClientSecret, Guid TillId, Guid TenantId);
    public sealed record DeviceTokenResult(string AccessToken, int ExpiresInSeconds);

    /// <summary>Thrown for the expected enrolment failures the API maps to 4xx.</summary>
    public sealed class EnrolmentException : Exception
    {
        public int StatusCode { get; }
        public EnrolmentException(int statusCode, string message) : base(message) => StatusCode = statusCode;
    }

    public sealed class EnrolmentOptions
    {
        /// <summary>HMAC signing secret for device tokens (same machinery as TestTokenAuth).</summary>
        public string DeviceTokenSecret { get; set; } = "";
        public int CodeTtlHours { get; set; } = 48;
        public int DeviceTokenTtlHours { get; set; } = 12;
    }

    /// <summary>
    /// T1.2 provisioning of tills + device enrolment + device-token issuance. Operates directly
    /// on the server-only tenancy tables (MySqlDbContext). All crypto is the shared SharedKernel
    /// primitives so this module never depends on the Identity module.
    /// </summary>
    public sealed class EnrolmentService
    {
        private readonly MySqlDbContext _db;
        private readonly EnrolmentOptions _opts;

        public EnrolmentService(MySqlDbContext db, EnrolmentOptions opts)
        {
            _db = db;
            _opts = opts;
        }

        private sealed class DeviceClaims
        {
            [JsonPropertyName("tid")] public string Tid { get; set; } = "";
            [JsonPropertyName("did")] public string Did { get; set; } = "";
            [JsonPropertyName("scope")] public string Scope { get; set; } = "device";
            [JsonPropertyName("exp")] public long Exp { get; set; }
        }

        /// <summary>Portal: create a till and a single-use enrolment code. Returns the plaintext
        /// code once (only its SHA-256 is stored).</summary>
        public async Task<CreateTillResult> CreateTillAsync(Guid tenantId, int storeId, string name, string actingUser)
        {
            _db.CurrentUser = actingUser;

            // WP11.1: the name is persisted in the server-only TillDetails side table (the legacy
            // Till POCO has no Name column). Enforce tenant-unique before writing → clean 409.
            name = name.Trim();
            var lowered = name.ToLowerInvariant();
            // Case-insensitive explicitly (LOWER()) so the guard behaves the same on MySQL (ci
            // collation) and SQLite (cs by default) — never relying on the column collation alone.
            if (await _db.TillDetails.IgnoreQueryFilters()
                    .AnyAsync(t => t.TenantId == tenantId && t.Name.ToLower() == lowered))
                throw new EnrolmentException(409, $"A till named '{name}' already exists.");

            var till = new Till { Id = Uuid7.New(), StoreId = storeId, LastOnline = DateTime.UtcNow };
            _db.Till.Add(till);
            _db.Entry(till).Property("TenantId").CurrentValue = tenantId; // shadow, tenant-owned
            _db.TillDetails.Add(new TillDetails { TillId = till.Id, TenantId = tenantId, Name = name });

            var code = Crockford32.NewCode(8);
            var enrolment = new EnrolmentCode
            {
                Id = Uuid7.New(),
                TenantId = tenantId,
                TillId = till.Id,
                CodeHash = CompactToken.Sha256(Crockford32.Normalise(code)),
                ExpiresAtUtc = DateTime.UtcNow.AddHours(_opts.CodeTtlHours),
                CreatedAtUtc = DateTime.UtcNow,
            };
            _db.EnrolmentCodes.Add(enrolment);

            await _db.SaveChangesAsync();
            return new CreateTillResult(till.Id, code, enrolment.ExpiresAtUtc);
        }

        /// <summary>WP11.1: rename a till (portal or the till itself). Tenant-unique, case-insensitive;
        /// a clash → 409. Upserts the TillDetails side row (older tills predate it).</summary>
        public async Task RenameTillAsync(Guid tenantId, Guid tillId, string name, string actingUser)
        {
            name = (name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name)) throw new EnrolmentException(400, "A till name is required.");
            if (name.Length > 80) throw new EnrolmentException(400, "Till name must be 80 characters or fewer.");

            var till = await _db.Till.FirstOrDefaultAsync(t => t.Id == tillId);
            if (till == null) throw new EnrolmentException(404, "Till not found.");

            var lowered = name.ToLowerInvariant();
            if (await _db.TillDetails.IgnoreQueryFilters()
                    .AnyAsync(t => t.TenantId == tenantId && t.Name.ToLower() == lowered && t.TillId != tillId))
                throw new EnrolmentException(409, $"A till named '{name}' already exists.");

            _db.CurrentUser = actingUser;
            var details = await _db.TillDetails.FirstOrDefaultAsync(t => t.TillId == tillId);
            if (details == null)
                _db.TillDetails.Add(new TillDetails { TillId = tillId, TenantId = tenantId, Name = name });
            else
                details.Name = name;
            await _db.SaveChangesAsync();
        }

        /// <summary>Anonymous: redeem an enrolment code, activate a device, return its client
        /// secret once. Reused/expired code → 410.</summary>
        public async Task<EnrolResult> EnrolAsync(string code, string actingUser)
        {
            var hash = CompactToken.Sha256(Crockford32.Normalise(code));
            var enrolment = await _db.EnrolmentCodes.FirstOrDefaultAsync(e => e.CodeHash == hash);

            if (enrolment == null) throw new EnrolmentException(410, "Unknown enrolment code.");
            if (enrolment.UsedAtUtc != null) throw new EnrolmentException(410, "Enrolment code already used.");
            if (enrolment.ExpiresAtUtc < DateTime.UtcNow) throw new EnrolmentException(410, "Enrolment code expired.");

            var secret = Crockford32.NewCode(24);
            var (secretHash, salt) = Pbkdf2.Hash(secret);
            var device = new Device
            {
                Id = Uuid7.New(),
                TenantId = enrolment.TenantId,
                TillId = enrolment.TillId,
                SecretHash = secretHash,
                SecretSalt = salt,
                Status = DeviceStatus.Active,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _db.Devices.Add(device);
            enrolment.UsedAtUtc = DateTime.UtcNow;

            _db.CurrentUser = actingUser;
            await _db.SaveChangesAsync();
            return new EnrolResult(device.Id, secret, device.TillId, device.TenantId);
        }

        /// <summary>Anonymous: exchange a device secret for a short-lived HMAC token carrying
        /// tid/did/scope. Revoked/unknown device or bad secret → 401.</summary>
        public async Task<DeviceTokenResult> IssueDeviceTokenAsync(Guid deviceId, string clientSecret)
        {
            var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId);
            // WP6.2: a PendingRemoval device keeps trading until an admin approves its removal —
            // only a Revoked (or unknown) device is refused a token.
            if (device == null || device.Status == DeviceStatus.Revoked)
                throw new EnrolmentException(401, "Device not enrolled or revoked.");
            if (!Pbkdf2.Verify(clientSecret, device.SecretSalt, device.SecretHash))
                throw new EnrolmentException(401, "Invalid device credentials.");

            var expiresIn = _opts.DeviceTokenTtlHours * 3600;
            var claims = new DeviceClaims
            {
                Tid = device.TenantId.ToString(),
                Did = device.Id.ToString(),
                Scope = "device",
                Exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresIn,
            };
            var token = CompactToken.Issue(JsonSerializer.Serialize(claims), _opts.DeviceTokenSecret);

            // WP13.1 usage metering: count the till coming online. Best-effort — a metering failure
            // must never stop a device getting its token. On this single-tenant host the device's
            // tenant matches the context so the write passes the guard; multi-tenant edge cases are
            // simply skipped by the catch until the drain path is made unscoped.
            try
            {
                await UsageMeter.AddAsync(_db, device.TenantId, DateOnly.FromDateTime(DateTime.UtcNow),
                    UsageMetrics.LoginsTill, 1);
                await _db.SaveChangesAsync();
            }
            catch { /* metering is best-effort */ }

            return new DeviceTokenResult(token, expiresIn);
        }

        /// <summary>WP: delete an EMPTY till (no recorded sales) — used to clean up tills created
        /// during enrolment testing. Refuses (409) a till that has sales, so trading history is
        /// never orphaned. Removes its devices, enrolment codes and the name row too.</summary>
        public async Task DeleteTillAsync(Guid tillId, string actingUser)
        {
            var till = await _db.Till.FirstOrDefaultAsync(t => t.Id == tillId);
            if (till == null) throw new EnrolmentException(404, "Till not found.");
            if (await _db.SalesV2.IgnoreQueryFilters().AnyAsync(s => s.TillId == tillId))
                throw new EnrolmentException(409, "This till has recorded sales and cannot be deleted (its history must be kept).");

            _db.CurrentUser = actingUser;
            _db.Devices.RemoveRange(await _db.Devices.Where(d => d.TillId == tillId).ToListAsync());
            _db.EnrolmentCodes.RemoveRange(await _db.EnrolmentCodes.Where(e => e.TillId == tillId).ToListAsync());
            var details = await _db.TillDetails.FirstOrDefaultAsync(t => t.TillId == tillId);
            if (details != null) _db.TillDetails.Remove(details);
            _db.Till.Remove(till);
            await _db.SaveChangesAsync();
        }

        /// <summary>Portal: revoke every device enrolled to a till; token issuance then refuses.</summary>
        public async Task<int> RevokeTillAsync(Guid tillId, string actingUser)
        {
            var devices = await _db.Devices.Where(d => d.TillId == tillId && d.Status == DeviceStatus.Active).ToListAsync();
            foreach (var d in devices) d.Status = DeviceStatus.Revoked;
            _db.CurrentUser = actingUser;
            await _db.SaveChangesAsync();
            return devices.Count;
        }
    }
}
