#nullable disable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;

namespace Plutus.Tenancy
{
    /// <summary>
    /// **Is the front door open?**
    ///
    /// ⚠⚠ THIS EXISTS BECAUSE THE SIGNUP API WAS ALREADY PUBLIC THE MOMENT IT SHIPPED, WITH NO
    /// LANDING PAGE ANYWHERE. Caddy proxies `/api/*` on `plutus.huggett.dscloud.me` straight to the
    /// backend, and the signup routes are `[AllowAnonymous]` — so on 2026-08-23 a POST from outside
    /// created an application successfully. "Not externally facing" was not true of the API, and
    /// building the page later would not have changed that.
    ///
    /// ⚠⚠ AND CADDY CANNOT FIX IT WITHOUT COLLATERAL DAMAGE. The till, the web till and the portal
    /// all share `/api/*` on that host; a path matcher carving out `/api/v1/signup` would work but
    /// puts a security control in a file that needs SSH to change, is invisible from the portal, and
    /// is a second place to remember. Matt asked whether this could be an operator-portal setting
    /// instead: it can, it should be, and it is — `Platform → Flags`, flag `signup.public`.
    ///
    /// ⚠⚠ CLOSED IS THE DEFAULT, AND THAT IS THE OPPOSITE OF EVERY OTHER FLAG HERE. The rest of
    /// `PlatformFlags` are kill switches: absent means the feature works, and setting Enabled=false
    /// turns it off. A front door read that way would be OPEN until somebody remembered to close
    /// it — which is the state this class was written to end. So **absent means closed**, and the
    /// flag has to be created and enabled deliberately.
    ///
    /// ⚠ A CLOSED DOOR ANSWERS 404, NOT 403. A 403 advertises that a signup API exists and is
    /// merely switched off, which is an invitation to come back later; a 404 says there is nothing
    /// here. The operator queue is not a thing to tell strangers about.
    /// </summary>
    public sealed class SignupGate
    {
        /// <summary>⚠ The flag name, as it appears in Platform → Flags. Public so the portal can
        /// show it by name and a test can assert on it rather than on a copy of the string.</summary>
        public const string FlagName = "signup.public";

        private readonly MySqlDbContext _db;
        public SignupGate(MySqlDbContext db) => _db = db;

        /// <summary>
        /// True only if the flag exists AND is enabled. ⚠ Any doubt resolves to closed: a missing
        /// row, a disabled row, or a database that cannot be read all mean "no".
        /// </summary>
        public async Task<bool> IsOpenAsync(CancellationToken ct = default)
        {
            try
            {
                return await _db.PlatformFlags.AsNoTracking()
                    .AnyAsync(f => f.FlagName == FlagName && f.Enabled, ct);
            }
            catch
            {
                // ⚠ Fail CLOSED. A signup endpoint that opens itself when the database hiccups is
                // worse than one that is briefly unavailable.
                return false;
            }
        }
    }
}
