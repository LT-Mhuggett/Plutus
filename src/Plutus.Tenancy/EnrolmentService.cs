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

            var till = new Till { Id = Uuid7.New(), StoreId = storeId, LastOnline = DateTime.UtcNow };
            _db.Till.Add(till);
            _db.Entry(till).Property("TenantId").CurrentValue = tenantId; // shadow, tenant-owned

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
            if (device == null || device.Status != DeviceStatus.Active)
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
            return new DeviceTokenResult(token, expiresIn);
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
