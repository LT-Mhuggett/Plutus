using CustomViews.Structs;
using Database.Enums;
using Database.Models;
using Microsoft.EntityFrameworkCore;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Helpers.Validators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Plutus.Frontend.AppClient.Helpers.Security
{
    /// <summary>
    /// ⚠ THE LEGACY PERMISSION GATE. <see cref="Services.Security.TillGate"/> replaced it at cutover
    /// step 12 and is what new code must use. This is kept only until the last caller is ported —
    /// see `Build/To do/MAUI-retrofit.md` §10 (L3).
    ///
    /// ⚠ IT CANNOT SUCCEED ON A PORTAL-PROVISIONED TILL, and until 2026-08-10 it did not merely fail
    /// — it CRASHED THE APP. It reads employees and an `AuthActions` table out of the legacy local
    /// database; a portal till has neither, so <c>GetEmployee</c> returned null and
    /// <c>emp.EmpAuths</c> threw. Every caller is an <c>async void</c> command with no <c>catch</c>,
    /// which makes that an unhandled exception and closes the till. "Change printer" did exactly
    /// this, from the settings screen, mid-shift.
    ///
    /// ⚠ So it now REFUSES instead of throwing. A permission check that cannot answer must answer
    /// "no" — never take the process with it, and never fall open.
    /// </summary>
    internal static class Authorisation
    {
        internal static bool IsAuthorised(this string eIdTemp, string action, Permissions rightNeeded, DatabaseProvider dbProvider) =>
            LegacyCheck(eIdTemp, action, dbProvider, (empAuth, _) => empAuth.Permissions.HasFlag(rightNeeded));

        internal static bool IsAuthorised(this string eIdTemp, string action, decimal amount, Permissions rightNeeded, DatabaseProvider dbProvider) =>
            LegacyCheck(eIdTemp, action, dbProvider,
                (empAuth, _) => empAuth.Auth?.Amount >= amount && empAuth.Permissions.HasFlag(rightNeeded));

        /// <summary>
        /// ⚠ Every step is null-checked and the whole thing is wrapped, because each of the four
        /// dereferences the original made — the employee, its grants, the action row, and the
        /// grant's ceiling — is null on a portal till, and any one of them ended the process.
        /// </summary>
        private static bool LegacyCheck(
            string eIdTemp, string action, DatabaseProvider dbProvider, Func<Emp_AuthActions, AuthActions, bool> verdict)
        {
            if (string.IsNullOrWhiteSpace(eIdTemp) || string.IsNullOrWhiteSpace(action)) return false;

            try
            {
                using (var dbHelper = new Database.Database(dbProvider))
                {
                    var emp = dbHelper.GetEmployee(eIdTemp);
                    if (emp?.EmpAuths == null) return false;

                    var authRequired = dbHelper.Get<AuthActions>()
                        .Where(aA => aA.Name.Equals(action)).SingleOrDefault();
                    if (authRequired == null) return false;

                    foreach (var empAuth in emp.EmpAuths)
                    {
                        if (empAuth == null || !empAuth.AuthAId.Equals(authRequired.Id))
                            continue;
                        return verdict(empAuth, authRequired);
                    }
                }
            }
            catch (Exception ex)
            {
                // ⚠ Refuse, and leave a trace. Silently returning false would hide the fact that a
                // screen is still on the legacy gate at all.
                Services.Analytics.CrashLog.Write("Authorisation.LegacyCheck", ex);
            }

            return false;
        }

        /// <summary>
        /// ⚠⚠ RETIRED 2026-08-19. It always refuses now, and that is an improvement on what it did
        /// before, which was **hang the till**.
        ///
        /// The old body could not succeed by any path, and each fault hid the next:
        ///
        ///   • <c>string authEmpId = default;</c> was **never assigned anywhere**, and the loop condition
        ///     was <c>while (string.IsNullOrEmpty(authEmpId))</c> — so a supervisor entering CORRECT
        ///     credentials was asked again, for ever. The only exit was cancelling the first prompt.
        ///   • The password prompt was handed <c>idElements</c> — <c>passElements</c> was built one line
        ///     above and never used — so it asked for an Employee ID while claiming to ask for a password.
        ///   • It read the answer as <c>.First().ToString()</c> on a <c>Dictionary&lt;uint, string&gt;</c>,
        ///     which yields the **KeyValuePair's** text (<c>"[1, secret]"</c>), not the password. So
        ///     <c>Password.Verify</c> compared the wrong string and could never match.
        ///   • <c>.First()</c> on the empty dictionary a back-out returns throws
        ///     <c>InvalidOperationException</c> — cancelling the password prompt crashed the caller.
        ///   • And it verified against a LOCAL <c>EmployeeModel</c> row, which a portal-provisioned till
        ///     does not have at all.
        ///
        /// ⚠ Three separate comments in this codebase already said this method "could not succeed" and
        /// "must not be ported" — <c>SupervisorPrompt</c>, <c>TillViewModel</c>'s override method, and
        /// <c>StoreOptionsViewModel</c>. It stayed wired to five live call sites regardless, where it
        /// read like a working supervisor gate.
        ///
        /// ⚠⚠ WHY REFUSE RATHER THAN REPAIR. The correct version of this already exists:
        /// <c>SupervisorPrompt.AskAsync</c> collects the credentials and
        /// <c>OperatorLogin.AuthoriseOverrideAsync</c> applies the rule — it refuses self-authorisation,
        /// applies the SUPERVISOR's own ceiling, window and staleness tier, and names both people.
        /// Rebuilding any of that here would be a second home for the override rule, which till-design
        /// C1 exists to prevent. **The callers must move**; work package in <c>MAUI-retrofit.md</c> §0.3b.
        ///
        /// ⚠ Every caller already treats a <c>default</c> return as "abandon the action"
        /// (<c>if (empAuthoriser == default) escape = true;</c>), so refusing is the shape they were
        /// written for — and a refusal an operator can read beats a spinner they cannot escape.
        /// </summary>
        internal static async Task<string> RequestAuthorisedUserInput(DatabaseProvider databaseProvider)
        {
            _ = databaseProvider;   // ⚠ Kept: the signature is what the five live call sites bind to.

            Services.Analytics.CrashLog.Write("Authorisation.RequestAuthorisedUserInput", null,
                "Legacy supervisor gate reached. It cannot authorise anybody and now refuses rather than "
                + "looping for ever. The caller needs migrating to SupervisorPrompt + "
                + "OperatorLogin.AuthoriseOverrideAsync.");

            await App.Current.MainPage.DisplayAlert(
                "Not supported on this till",
                "This action needs a supervisor to authorise it, and the old authorisation screen cannot "
                + "do that on a till set up from the portal.\n\nNothing has been changed — ask a supervisor "
                + "to sign in and make the change themselves.",
                "OK".Translate());

            // ⚠⚠ NEVER a non-null value. Returning an id would authorise an action against a supervisor
            // nobody verified, which is the one outcome worse than refusing.
            return default;
        }
    }
}
