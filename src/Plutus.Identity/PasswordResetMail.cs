using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.SharedKernel;

namespace Plutus.Identity
{
    /// <summary>
    /// FE9.1 reset / invite email. Rides the platform <see cref="IMessageSender"/> seam, so it is
    /// SIMULATED (journaled in MessageEvents, not delivered) until an Email provider is configured
    /// and enabled in Platform → Notifications — the same switch the MFA heads-up waits on.
    /// A mail failure never fails the caller: the token is already saved, and an admin can read the
    /// link out of the audit/journal or set a password directly.
    /// </summary>
    internal static class PasswordResetMail
    {
        public static async Task<bool> SendAsync(
            MySqlDbContext db, IMessageSender mail, Guid tenantId,
            string email, string name, string token, bool isInvite,
            HttpRequest request, CancellationToken ct = default)
        {
            try
            {
                // the tenant's own sending identity — never ride a shared domain (WP17.3 rule)
                var identity = await db.TenantSendingIdentities.AsNoTracking()
                    .FirstOrDefaultAsync(i => i.TenantId == tenantId, ct);
                var from = identity?.FromAddress ?? string.Empty;

                var link = ResetLink(request, token);
                var subject = isInvite ? "Set your Plutus password" : "Reset your Plutus password";
                var body =
                    (string.IsNullOrWhiteSpace(name) ? "Hello," : $"Hello {name},") + "\n\n" +
                    (isInvite
                        ? "An administrator has created a Plutus account for you. Use the link below to choose a password and sign in."
                        : "An administrator has started a password reset for your Plutus account. Use the link below to choose a new password.") +
                    "\n\n" + link + "\n\n" +
                    $"The link can be used once and expires in {PasswordResetService.Lifetime.TotalHours:0} hours.\n" +
                    "If you weren't expecting this, you can ignore this email — your current password still works.";

                var result = await mail.SendAsync(new OutboundMessage(tenantId, MessageChannel.Email, email, from, subject, body), ct);
                return result.Accepted;
            }
            catch
            {
                return false; // best-effort: the token is saved either way
            }
        }

        /// <summary>The portal's completion URL. The portal reads `#reset=<token>` on load.</summary>
        private static string ResetLink(HttpRequest request, string token)
        {
            var origin = request?.Headers["Origin"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(origin) && request != null)
                origin = $"{request.Scheme}://{request.Host}";
            return $"{(origin ?? string.Empty).TrimEnd('/')}/#reset={token}";
        }
    }
}
