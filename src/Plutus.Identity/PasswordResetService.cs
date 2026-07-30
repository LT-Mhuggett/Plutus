using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Identity
{
    /// <summary>
    /// FE9.1 password set / reset / invite.
    ///
    /// Tokens: 160 bits of randomness, Crockford32-encoded (URL-safe, and typeable if a user reads
    /// it off a screen), stored ONLY as a SHA-256 hash — a leaked backup cannot be replayed into
    /// account takeovers. Single-use with a 48h life; issuing or consuming one invalidates that
    /// user's other outstanding tokens, so an old link in an inbox stops working.
    ///
    /// Honest limitation (documented in the UI too): sessions are 12h bearer tokens with no
    /// server-side revocation list, so a reset stops the NEXT sign-in — it does not eject a
    /// session that is already live.
    /// </summary>
    public sealed class PasswordResetService
    {
        public const int TokenChars = 32;              // 32 Crockford chars ≈ 160 bits
        public static readonly TimeSpan Lifetime = TimeSpan.FromHours(48);
        public const int MinPasswordLength = 8;

        private readonly MySqlDbContext _db;
        public PasswordResetService(MySqlDbContext db) => _db = db;

        /// <summary>A fresh token: the plaintext (email this, never store it) and the row to save.</summary>
        public (string Token, PasswordResetToken Row) Mint(
            Guid tenantId, Guid userId, string email, bool isInvite, Guid? requestedBy)
        {
            var token = NewToken();
            var row = new PasswordResetToken
            {
                Id = Uuid7.New(), TenantId = tenantId, UserId = userId, Email = email,
                TokenHash = CompactToken.Sha256(Normalise(token)),
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.Add(Lifetime),
                IsInvite = isInvite, RequestedBy = requestedBy,
            };
            return (token, row);
        }

        /// <summary>Invalidate every outstanding token for a user (called when issuing a new one and
        /// after a successful completion) — one live link at a time.</summary>
        public async Task InvalidateOutstandingAsync(Guid userId, CancellationToken ct = default)
        {
            var live = await _db.PasswordResetTokens.IgnoreQueryFilters()
                .Where(t => t.UserId == userId && t.UsedAtUtc == null)
                .ToListAsync(ct);
            foreach (var t in live) t.UsedAtUtc = DateTime.UtcNow;
        }

        public enum CompleteOutcome { Ok, UnknownToken, Expired, AlreadyUsed, WeakPassword, NoAccount }

        /// <summary>
        /// Consume a token and set the password. Anonymous path — the caller has no tenant context,
        /// hence IgnoreQueryFilters throughout. Creates the WebCredential when the user never had a
        /// login (the invite case).
        /// </summary>
        public async Task<CompleteOutcome> CompleteAsync(string token, string newPassword, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(token)) return CompleteOutcome.UnknownToken;
            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < MinPasswordLength)
                return CompleteOutcome.WeakPassword;

            var hash = CompactToken.Sha256(Normalise(token));
            var row = await _db.PasswordResetTokens.IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            if (row == null) return CompleteOutcome.UnknownToken;
            if (row.UsedAtUtc != null) return CompleteOutcome.AlreadyUsed;
            if (row.ExpiresAtUtc < DateTime.UtcNow) return CompleteOutcome.Expired;

            var user = await _db.Employees.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == row.UserId, ct);
            if (user == null || !user.Active) return CompleteOutcome.NoAccount;

            await SetPasswordAsync(row.UserId, row.Email, newPassword, ct);
            row.UsedAtUtc = DateTime.UtcNow;
            // any sibling token dies with it
            await InvalidateOutstandingAsync(row.UserId, ct);

            _db.CurrentUser = row.UserId.ToString(); // the user acted on their own account
            _db.Audit(row.TenantId, row.UserId, "user.password.reset.complete", nameof(Employee), row.UserId.ToString(),
                new { row.Email, row.IsInvite });
            await _db.SaveChangesAsync(ct);
            return CompleteOutcome.Ok;
        }

        /// <summary>
        /// Upsert the user's web login with a new password. Also GRANTS a login to a staff user who
        /// never had one (the admin-set and invite cases). Does NOT save — the caller composes the
        /// audit row and saves once.
        /// </summary>
        public async Task SetPasswordAsync(Guid userId, string email, string password, CancellationToken ct = default)
        {
            var (hash, salt) = Pbkdf2.Hash(password);
            var cred = await _db.WebCredentials.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.EmployeeId == userId, ct);
            if (cred == null)
            {
                _db.WebCredentials.Add(new WebCredential
                {
                    Email = email, EmployeeId = userId,
                    HashedPassword = Convert.ToBase64String(hash), Salt = Convert.ToBase64String(salt),
                });
            }
            else
            {
                cred.HashedPassword = Convert.ToBase64String(hash);
                cred.Salt = Convert.ToBase64String(salt);
            }
        }

        /// <summary>Crockford32 so the token survives being read aloud or line-wrapped in an email.</summary>
        private static string NewToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(TokenChars);
            var chars = new char[TokenChars];
            for (var i = 0; i < TokenChars; i++) chars[i] = Crockford32.Alphabet[bytes[i] & 0x1f];
            return new string(chars);
        }

        /// <summary>Fold the ambiguous characters so a hand-typed token still matches its hash.</summary>
        private static string Normalise(string token) => Crockford32.Normalise(token);
    }
}
