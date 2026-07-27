using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Plutus.DBService.Extensions;
using Plutus.Identity;
using Plutus.SharedKernel;
using System;
using System.Threading.Tasks;

namespace Plutus.DBService.Controllers
{
    /// <summary>
    /// TEST-ENVIRONMENT login (see TestTokenAuth.cs). Verifies credentials held in the
    /// WebCredentials table (seeded from the till backup) and issues an HMAC token.
    /// No [Authorize] — this is the entry point. Replaced by B2C once available.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly EffectivePermissionsService _permissions;

        public AuthController(IConfiguration configuration, EffectivePermissionsService permissions)
        {
            _configuration = configuration;
            _permissions = permissions;
        }

        public class LoginRequest
        {
            public string Email { get; set; }
            public string Password { get; set; }
        }

        public class SetPasswordRequest
        {
            public Guid EmployeeId { get; set; }
            public string Email { get; set; }
            public string Password { get; set; }
        }

        /// <summary>Create or replace an employee's web login. Requires a signed-in user.</summary>
        [Microsoft.AspNetCore.Authorization.Authorize]
        [HttpPost("SetPassword")]
        public async Task<ActionResult> SetPassword([FromBody] SetPasswordRequest request)
        {
            if (request == null || request.EmployeeId == Guid.Empty ||
                string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
                return BadRequest("EmployeeId, email and password are required.");
            if (request.Password.Length < 8)
                return BadRequest("Password must be at least 8 characters.");

            var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(64);
#pragma warning disable SYSLIB0041 // matches the legacy till KDF
            using var kdf = new System.Security.Cryptography.Rfc2898DeriveBytes(request.Password, salt);
#pragma warning restore SYSLIB0041
            kdf.IterationCount = 101010;
            var hash = kdf.GetBytes(64);

            await using var conn = new MySqlConnection(_configuration["ConnectionString"]);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"REPLACE INTO WebCredentials (Email, EmployeeId, HashedPassword, Salt)
                                VALUES (@email, @empId, @hash, @salt)";
            cmd.Parameters.AddWithValue("@email", request.Email.Trim());
            cmd.Parameters.AddWithValue("@empId", request.EmployeeId.ToString());
            cmd.Parameters.AddWithValue("@hash", Convert.ToBase64String(hash));
            cmd.Parameters.AddWithValue("@salt", Convert.ToBase64String(salt));
            await cmd.ExecuteNonQueryAsync();

            return Ok();
        }

        [HttpPost("Login")]
        public async Task<ActionResult> Login([FromBody] LoginRequest request)
        {
            var secret = _configuration["TEST_TOKEN_SECRET"];
            if (string.IsNullOrEmpty(secret))
                return StatusCode(503, "Login is not configured on this server.");
            if (string.IsNullOrWhiteSpace(request?.Email) || string.IsNullOrEmpty(request?.Password))
                return BadRequest("Email and password are required.");

            await using var conn = new MySqlConnection(_configuration["ConnectionString"]);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT w.EmployeeId, w.HashedPassword, w.Salt, p.FName, p.LName
                FROM WebCredentials w
                JOIN People p ON p.Id = w.EmployeeId
                WHERE LOWER(w.Email) = LOWER(@email)
                LIMIT 1";
            cmd.Parameters.AddWithValue("@email", request.Email.Trim());

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return Unauthorized("Unknown email or wrong password.");

            var employeeId = reader.GetGuid(0);
            var hash = Convert.FromBase64String(reader.GetString(1));
            var salt = Convert.FromBase64String(reader.GetString(2));
            var name = $"{reader.GetString(3)} {reader.GetString(4)}".Trim();

            if (!TestTokenAuth.VerifyPassword(request.Password, salt, hash))
                return Unauthorized("Unknown email or wrong password.");

            // The credentials reader must be closed before the connection runs another command.
            await reader.CloseAsync();

            // WP10.2 (D16): a Suspended/Closed tenant's PORTAL login is refused — but its tills keep
            // syncing, because the device-token path (TokensController) never consults this. The
            // user's tenant is the TenantId shadow on their People row.
            var tenantId = Guid.Empty;
            await using (var statusCmd = conn.CreateCommand())
            {
                statusCmd.CommandText =
                    "SELECT t.Id, t.Status FROM Tenants t JOIN People p ON p.TenantId = t.Id WHERE p.Id = @empId LIMIT 1";
                statusCmd.Parameters.AddWithValue("@empId", employeeId.ToString());
                await using var statusReader = await statusCmd.ExecuteReaderAsync();
                if (await statusReader.ReadAsync())
                {
                    tenantId = statusReader.GetGuid(0);
                    var status = statusReader.GetByte(1);
                    await statusReader.CloseAsync();
                    if (status == 3 /*Suspended*/ || status == 4 /*Closed*/)
                        return StatusCode(403, "Your organisation's portal access is suspended. Please contact billing.");
                }
            }

            // WP3.1 (2026-07-24): token scopes come from the user's RBAC effective permissions
            // (time windows evaluated NOW — an out-of-window assignment grants nothing at token
            // issue, per architecture §7.2), with the WP2.2 pre-seed fallback for users without
            // assignments. Phase 9: the SAME resolver feeds the IdP claims-transformation, so an
            // operator gets identical scopes whether logged in by password here or by a real IdP.
            var scope = string.Join(" ", await _permissions.ResolveLoginScopesAsync(employeeId, DateTime.Now));

            var payload = new TestTokenAuth.TokenPayload
            {
                EmployeeId = employeeId,
                Name = name,
                Scope = scope,
                Exp = DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds(),
            };

            // WP13.1 usage metering: count the portal login. Raw upsert on the SAME connection
            // (bypasses the EF tenant guard) and best-effort — a metering hiccup must NEVER fail a
            // login. Keyed on (TenantId, BusinessDay, Metric) so ON DUPLICATE KEY just increments.
            if (tenantId != Guid.Empty)
            {
                try
                {
                    await using var meterCmd = conn.CreateCommand();
                    meterCmd.CommandText = @"INSERT INTO TenantUsageRollups (TenantId, BusinessDay, Metric, Value)
                                             VALUES (@tid, @day, @metric, 1)
                                             ON DUPLICATE KEY UPDATE Value = Value + 1";
                    meterCmd.Parameters.AddWithValue("@tid", tenantId.ToString());
                    meterCmd.Parameters.AddWithValue("@day", DateTime.UtcNow.Date);
                    meterCmd.Parameters.AddWithValue("@metric", Plutus.Entities.Models.UsageMetrics.LoginsPortal);
                    await meterCmd.ExecuteNonQueryAsync();
                }
                catch { /* metering is best-effort — never break authentication */ }
            }

            return Ok(new
            {
                token = TestTokenAuth.Issue(payload, secret),
                employeeId,
                name,
                expiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.Exp),
            });
        }
    }
}
