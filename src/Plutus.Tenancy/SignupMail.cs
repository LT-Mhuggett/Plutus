#nullable disable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Plutus.SharedKernel;

namespace Plutus.Tenancy
{
    /// <summary>
    /// The signup emails — verification, and the rejection note.
    ///
    /// ⚠ Rides the platform <see cref="IMessageSender"/> seam, so it is SIMULATED (journaled in
    /// MessageEvents, not delivered) until an Email provider is configured and enabled in
    /// Platform → Notifications. That is the same state `PasswordResetMail` is in.
    ///
    /// ⚠⚠ A MAIL FAILURE NEVER FAILS THE CALLER, and it is a DoD item rather than a nicety: *"the
    /// signup path is tested with the mail sender FAILING — a bounced verification email must leave a
    /// recoverable application, not a dead row."* The application row is already committed by the
    /// time this runs; the operator queue can resend.
    ///
    /// ⚠ `Guid.Empty` as the tenant id throughout: there IS no tenant. That is the whole premise of
    /// stage 1, and the message journal carries these as platform-level events.
    /// </summary>
    public static class SignupMail
    {
        public static async Task<bool> SendVerifyAsync(
            IMessageSender mail, TenantApplicationService _unused, string email, string name,
            string token, HttpRequest request, CancellationToken ct = default)
        {
            try
            {
                if (mail == null || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token)) return false;

                var link = VerifyLink(request, token);
                var body =
                    (string.IsNullOrWhiteSpace(name) ? "Hello," : $"Hello {name},") + "\n\n" +
                    "Thanks for applying for a Plutus account. Confirm this email address to continue:\n\n" +
                    link + "\n\n" +
                    $"The link can be used once and expires in {TenantApplicationService.VerifyLifetime.TotalHours:0} hours.\n\n" +
                    "Confirming your address does not create an account — we review every application " +
                    "before setting one up, and we will email you either way.\n\n" +
                    "If you didn't apply, you can ignore this email. Nothing has been created.";

                var result = await mail.SendAsync(
                    new OutboundMessage(Guid.Empty, MessageChannel.Email, email, string.Empty,
                                        "Confirm your email address — Plutus", body), ct);
                return result.Accepted;
            }
            catch
            {
                return false;   // best-effort, deliberately: the application row is already saved
            }
        }

        /// <summary>⚠ The reason is the applicant's, not an internal note — it is written by an
        /// operator knowing it will be read by the person rejected.</summary>
        public static async Task<bool> SendRejectionAsync(
            IMessageSender mail, string email, string name, string businessName, string reason,
            CancellationToken ct = default)
        {
            try
            {
                if (mail == null || string.IsNullOrWhiteSpace(email)) return false;

                var body =
                    (string.IsNullOrWhiteSpace(name) ? "Hello," : $"Hello {name},") + "\n\n" +
                    $"Thanks for your interest in Plutus. We're not able to set up an account for " +
                    $"{businessName} at the moment.\n\n" +
                    reason + "\n\n" +
                    "If you think this is a mistake, reply to this email and we'll take another look.";

                var result = await mail.SendAsync(
                    new OutboundMessage(Guid.Empty, MessageChannel.Email, email, string.Empty,
                                        "About your Plutus application", body), ct);
                return result.Accepted;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// ⚠ Points at the SIGNUP site, which is WP-LANDING and does not exist yet. Until it does,
        /// the link resolves against the request's own origin — so the token is always reachable
        /// (from the journal if nothing else) rather than being minted into a URL that goes nowhere.
        /// `SIGNUP_BASE_URL` overrides it once the landing site is live.
        /// </summary>
        private static string VerifyLink(HttpRequest request, string token)
        {
            var configured = Environment.GetEnvironmentVariable("SIGNUP_BASE_URL");
            var origin = !string.IsNullOrWhiteSpace(configured)
                ? configured
                : request?.Headers["Origin"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(origin) && request != null)
                origin = $"{request.Scheme}://{request.Host}";

            return $"{(origin ?? string.Empty).TrimEnd('/')}/#verify={token}";
        }
    }
}
