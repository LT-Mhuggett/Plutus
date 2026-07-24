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
    internal static class Authorisation
    {
        internal static bool IsAuthorised(this string eIdTemp, string action, Permissions rightNeeded, DatabaseProvider dbProvider)
        {
            using (var dbHelper = new Database.Database(dbProvider))
            {
                var emp = dbHelper.GetEmployee(eIdTemp);
                var authRequired = dbHelper.Get<AuthActions>().Where(aA => aA.Name.Equals(action)).SingleOrDefault();

                foreach (var empAuth in emp.EmpAuths)
                {
                    if (!empAuth.AuthAId.Equals(authRequired.Id))
                        continue;
                    return empAuth.Permissions.HasFlag(rightNeeded);
                }
            }
            return false;
        }

        internal static bool IsAuthorised(this string eIdTemp, string action, decimal amount, Permissions rightNeeded, DatabaseProvider dbProvider)
        {
            using (var dbHelper = new Database.Database(dbProvider))
            {
                var emp = dbHelper.GetEmployee(eIdTemp);
                var authRequired = dbHelper.Get<AuthActions>().Where(aA => aA.Name.Equals(action)).SingleOrDefault();

                foreach (var empAuth in emp.EmpAuths)
                {
                    if (!empAuth.AuthAId.Equals(authRequired.Id))
                        continue;
                    if (empAuth.Auth.Amount < amount)
                        continue;
                    return empAuth.Permissions.HasFlag(rightNeeded);
                }
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
