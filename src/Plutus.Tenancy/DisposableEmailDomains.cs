#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Plutus.Tenancy
{
    public interface IDisposableEmailDomains
    {
        bool IsDisposable(string email);
        IReadOnlyCollection<string> Domains { get; }
    }

    /// <summary>
    /// **The throwaway-inbox blocklist.**
    ///
    /// ⚠⚠ A LIST, NOT A REGEX, AND CONFIG RATHER THAN CODE — WP-signup §3.2 is explicit about both.
    /// Email verification is the control that proves a person; it is a formality if the inbox is a
    /// ten-minute one. And the list is wrong the day after it ships, so it must be updatable without
    /// a deploy: `SIGNUP_DISPOSABLE_DOMAINS`, comma-separated, overrides the seed below entirely.
    ///
    /// ⚠ It is a speed bump, not a wall, and is documented as one so nobody leans on it. Anybody
    /// determined will find a domain that is not listed. What it stops is the CASUAL throwaway, which
    /// is the bulk of the junk — and the operator queue (§6) is the real backstop for the rest.
    ///
    /// ⚠ SUBDOMAINS COUNT. `mail.mailinator.com` is mailinator; matching the exact host only would
    /// be bypassed by a wildcard MX, which several of these services offer as a feature.
    /// </summary>
    public sealed class DisposableEmailDomains : IDisposableEmailDomains
    {
        /// <summary>A deliberately short seed of the most common services. ⚠ Not a curated
        /// blocklist and not trying to be — override it in config when the queue tells you what is
        /// actually arriving.</summary>
        private static readonly string[] Seed =
        {
            "mailinator.com", "guerrillamail.com", "10minutemail.com", "tempmail.com",
            "temp-mail.org", "throwawaymail.com", "yopmail.com", "trashmail.com",
            "getnada.com", "sharklasers.com", "dispostable.com", "maildrop.cc",
            "fakeinbox.com", "mintemail.com", "spamgourmet.com", "mailnesia.com",
        };

        private readonly HashSet<string> _domains;

        public DisposableEmailDomains(string configured)
        {
            var list = string.IsNullOrWhiteSpace(configured)
                ? Seed
                : configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            _domains = new HashSet<string>(
                list.Select(d => d.Trim().TrimStart('@', '.').ToLowerInvariant()).Where(d => d.Length > 0),
                StringComparer.Ordinal);
        }

        public IReadOnlyCollection<string> Domains => _domains;

        public bool IsDisposable(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            var at = email.LastIndexOf('@');
            if (at < 0 || at == email.Length - 1) return false;

            var host = email[(at + 1)..].Trim().TrimEnd('.').ToLowerInvariant();
            if (host.Length == 0) return false;

            // exact, then every parent domain — so mail.mailinator.com matches mailinator.com
            if (_domains.Contains(host)) return true;
            for (var i = host.IndexOf('.'); i > 0 && i < host.Length - 1; i = host.IndexOf('.', i + 1))
                if (_domains.Contains(host[(i + 1)..])) return true;

            return false;
        }
    }
}
