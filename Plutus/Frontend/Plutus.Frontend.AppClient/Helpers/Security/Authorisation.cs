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
    /// see `Build/MAUI-retrofit.md` §10 (L3).
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

        internal static async Task<string> RequestAuthorisedUserInput(DatabaseProvider databaseProvider)
        {
            var empIDValidators = new IValidator[]
            {
                new RequiredValidator()
            };

            var idElements = new ViewElementData[]
            {
                new ViewElementData(1, string.Format("IdArg".Translate(), "Employee".Translate()), "", empIDValidators.AsEnumerable(), false, true)
            };

            string authEmpId = default;

            do
            {
                (await CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                    idElements,
                    "Confirm".Translate(),
                    true,
                    "AuthReq".Translate(),
                    "Cancel".Translate())).TryGetValue(1, out var datumId);

                if (datumId == default)
                    break;

                var authEmp = App.GetViewModel().Employees.FirstOrDefault(e => e.Id.Equals(datumId));
                if (authEmp == null)
                {
                    using (var db = new Database.Database(databaseProvider))
                    {
                        authEmp = await db.Get<EmployeeModel>()
                                    .SingleOrDefaultAsync(e =>
                                        e.Id.Equals(datumId) ||
                                        e.Email.Equals(datumId, StringComparison.CurrentCultureIgnoreCase));
                        if (authEmp != null)
                        {
                            var passValidators = new IValidator[]
                            {
                                new RequiredValidator(),
                                new PasswordValidator()
                            };

                            var passElements = new Tuple<string, string, IEnumerable<IValidator>, bool, bool>[]
                            {
                                Tuple.Create("Password".Translate(), "", passValidators.AsEnumerable(), true, true)
                            };

                            var datumPass = (await CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                                idElements,
                                "Confirm".Translate(),
                                true,
                                "PassConf".Translate(),
                                "Cancel".Translate())).First().ToString();
                            if (datumPass == default)
                                return default;

                            if (!await Task.Run(() =>
                                Password.Verify(datumPass, Convert.FromBase64String(authEmp.Salt),
                                    Convert.FromBase64String(authEmp.HashedPassword))))
                            {
                                await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "DetailsNotCorrectORUserNotExistMesg".Translate(), "OK".Translate());
                            }
                        }
                    }
                }
            } while (string.IsNullOrEmpty(authEmpId));

            return authEmpId;
        }
    }
}
