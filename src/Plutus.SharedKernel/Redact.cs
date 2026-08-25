using System.Text.RegularExpressions;

namespace Plutus.SharedKernel
{
    /// <summary>
    /// Strip credentials out of strings before they are logged.
    ///
    /// ⚠⚠ WHY THIS EXISTS. `Startup.Configure` printed the whole MySQL connection string —
    /// password included — on every boot, ungated by environment, straight into
    /// `~/.pm2/logs/plutus-backend-out.log`. 37 occurrences across 15 rotated files by the time it
    /// was noticed on 2026-08-25.
    ///
    /// ⚠ It arrived with commit 3d2837a2, *"Rebuild branch on upstream/master and remove
    /// secret-bearing history"*. **Scrubbing a secret out of git says nothing about the code that
    /// reprints it every time the process starts.**
    ///
    /// ⚠ It lives in SharedKernel rather than beside its one caller so the unit suite can reach it:
    /// the test project does not reference the web host, and a redaction nobody tests is a
    /// redaction that fails open the first time somebody uses a spelling it does not know.
    /// </summary>
    public static class Redact
    {
        /// <summary>
        /// ⚠ Every spelling an ADO/MySQL connection string can use for the secret. Missing one means
        /// the credential prints, so this errs toward matching: `Password`, `Pwd`, and the spaced
        /// `User Password` form.
        /// ⚠ `[^;]*` — terminated by the separator, never greedy. A greedy match would swallow the
        /// rest of the string and hide the server and database, which is the half worth keeping.
        /// ⚠ Case-insensitive, and tolerant of spaces around the `=`, because connection strings in
        /// the wild have both.
        /// </summary>
        private static readonly Regex Secrets = new(
            @"(?i)\b(password|pwd|user\s+password)\s*=\s*[^;]*",
            RegexOptions.Compiled);

        public const string Mask = "***REDACTED***";

        /// <summary>
        /// Replace any credential in a connection string with <see cref="Mask"/>, keeping everything
        /// else — the server, protocol and database are the reason the line is logged at all.
        /// </summary>
        public static string ConnectionString(string? connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) return connectionString ?? string.Empty;
            return Secrets.Replace(connectionString, "$1=" + Mask);
        }
    }
}
