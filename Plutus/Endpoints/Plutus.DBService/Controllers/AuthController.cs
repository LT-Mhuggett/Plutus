using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Plutus.DBService.Extensions;
using Plutus.Identity;
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

        public AuthController(IConfiguration configuration)
        {
            _configuration = configuration;
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

            // WP1.2 follow-up (decision 2026-07-24): every operator gets the unambiguous
            // capability `pos.sell`. WP2.2 (decision 2026-07-24): employees holding the legacy
            // Admin or Management AuthAction ALSO get `portal.tills.enrol` so they can create
            // tills/enrolment codes from the web POS — the minimal slice of the AuthActions→
            // scope mapping that WP3.1 RBAC replaces wholesale.
            var scope = "pos.sell";
            await using (var scopeCmd = conn.CreateCommand())
            {
                scopeCmd.CommandText = @"
                    SELECT COUNT(*) FROM EmpAuthActions ea
                    JOIN AuthActions a ON a.Id = ea.AuthAId
                    WHERE ea.EmpId = @empId AND a.Name IN ('Admin', 'Management')";
                scopeCmd.Parameters.AddWithValue("@empId", employeeId.ToString());
                var isAdmin = Convert.ToInt64(await scopeCmd.ExecuteScalarAsync()) > 0;
                if (isAdmin) scope += " portal.tills.enrol";
            }

            var payload = new TestTokenAuth.TokenPayload
            {
                EmployeeId = employeeId,
                Name = name,
                Scope = scope,
                Exp = DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds(),
            };

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
