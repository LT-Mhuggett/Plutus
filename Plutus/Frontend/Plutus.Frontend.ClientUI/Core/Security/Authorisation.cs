using Plutus.Contracts;
using Plutus.Entities.Enums;
using System;
using System.Threading.Tasks;
using Permissions = Plutus.Entities.Enums.Permissions;

namespace Plutus.Frontend.ClientUI.Core.Security
{
    internal class Authorisation
    {
        private IRepositoryWrapper RepositoryWrapper;
        private IAppState _appState;

        internal Authorisation(IRepositoryWrapper repositoryWrapper, IAppState appState)
        {
            RepositoryWrapper = repositoryWrapper;
        }

        internal async Task<bool> IsAuthorised(Guid eIdTemp, string action, Permissions rightNeeded)
        {
            var emp = await RepositoryWrapper.EmployeeRepository.FindById(eIdTemp, _appState.Business.Id);
            if (emp == default)
                return false;

            var authRequired = await RepositoryWrapper.AuthActionRepository.FindFirstByCondition(aA => aA.Name.Equals(action));

            foreach (var empAuth in emp.EmpAuths)
            {
                if (!empAuth.AuthAId.Equals(authRequired.Id))
                    continue;
                return empAuth.Permissions.HasFlag(rightNeeded);
            }
            /*
            using (var dbHelper = new Database.Database(dbStyle))
            {
                var emp = dbHelper.GetEmployee(eIdTemp);
                var authRequired = dbHelper.Get<AuthActions>().Where(aA => aA.Name.Equals(action)).SingleOrDefault();

                foreach (var empAuth in emp.EmpAuths)
                {
                    if (!empAuth.AuthAId.Equals(authRequired.Id))
                        continue;
                    return empAuth.Permissions.HasFlag(rightNeeded);
                }
            }*/
            return false;
        }

        internal async Task<bool> IsAuthorised(Guid eIdTemp, string action, decimal amount, Permissions rightNeeded)
        {
            var emp = await RepositoryWrapper.EmployeeRepository.FindById(eIdTemp, _appState.Business.Id);
            if (emp == default)
                return false;

            var authRequired = await RepositoryWrapper.AuthActionRepository.FindFirstByCondition(aA => aA.Name.Equals(action));

            foreach (var empAuth in emp.EmpAuths)
            {
                if (!empAuth.AuthAId.Equals(authRequired.Id))
                    continue;
                if (empAuth.AuthA.Amount < amount)
                    continue;
                return empAuth.Permissions.HasFlag(rightNeeded);
            }
            /*
            using (var dbHelper = new Database.Database(dbStyle))
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
            }*/
            return false;
        }

        internal async Task<string> RequestAuthorisedUserInput()
        {
            throw new NotImplementedException();
            /*
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
                    authEmp = await RepositoryWrapper.RepositoryInstance.EmployeeRepository.FindById(datumId);
                    if (authEmp != default)
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

                        var datumPass = (await CustomViews.InputAlertHelper.ShowAlertDialogAsync(idElements,
                                                                                                 "Confirm".Translate,
                                                                                                 true,
                                                                                                 "PassConf".Translate,
                                                                                                 "Cancel".Translate)).First().ToString();
                        if (datumPass == default)
                            return default;

                        if (!await Task.Run(() =>
                                Password.Verify(datumPass, Convert.FromBase64String(authEmp.Salt),
                                    Convert.FromBase64String(authEmp.HashedPassword))))
                        {
                            await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "DetailsNotCorrectORUserNotExistMesg".Translate(), "OK".Translate());
                        }
                    }/*
                    using (var db = new Database.Database(dbStyle))
                    {
                        authEmp = await db.Get<Employee>()
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
            */
        }
    }
}
