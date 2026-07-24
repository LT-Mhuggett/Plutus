using CommunityToolkit.Mvvm.Input;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Core.Extensions;
using Plutus.Frontend.ClientUI.Pages.MainTill;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Frontend.ClientUI.Services.Authentication;
using Plutus.Frontend.ClientUI.Services.Loading;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using Plutus.Frontend.ClientUI.Resources.I18N_L10N;
using Microsoft.Identity.Client;
using Plutus.Repository.QueryParameters;
using Plutus.Frontend.ClientUI.Services.Repository.Contracts;
using Plutus.Entities.Models;
using Plutus.Frontend.ClientUI.Helpers;

namespace Plutus.Frontend.ClientUI.ViewModels
{
    public partial class LoginViewModel : BaseViewModel
    {
        #region Properties
        public IAuthService AuthService { get; }

        // Controls visibility of the DEBUG-only dev login bypass button (see DevSkipLogin).
#if DEBUG
        public bool IsDebugBuild => true;
#else
        public bool IsDebugBuild => false;
#endif
        #endregion

        public LoginViewModel(ILogger logger, IAppState appState, LoadingViewService loadingViewService, IRepositoryWrapper repositoryWrapper, IAuthService authService) : base(logger, appState, loadingViewService, repositoryWrapper)
        {
            Title = Strings.Login;
            AuthService = authService;
        }

        #region Commands
        /*
        [ICommand]
        private void ExecuteShowLoggedUsers()
        {
            throw new NotImplementedException();
            //Implement Show Logged Users Page
        }*/

        [RelayCommand]
        private async void Login()
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromSeconds(120));
                var (authResult, account) = await AuthService.LoginAsync(cancellationTokenSource.Token);
                var token = authResult?.AccessToken;
                if(token != null)
                {
                    var handler = new JwtSecurityTokenHandler();
                    var data = handler.ReadJwtToken(token);
                    var claims = data.Claims.ToList();
                    if (data != null)
                    {
                        AppState.CurrentActiveUser = new KeyValuePair<Guid, string>(Guid.Parse(authResult.UniqueId), token);
                        RepositoryWrapper.SetCurrentUser(AppState.CurrentActiveUser.Key.ToString());
                        if (await LoadDataIn(authResult))
                            App.Current.MainPage = ServiceHelper.GetService<MainPage>();
                    }
                }
            }
            catch (Exception ex)
            {
                Microsoft.AppCenter.Crashes.Crashes.TrackError(ex);
                Debug.WriteLine(ex.Message);
            }

            finally
            {
                IsBusy = false;
            }
        }

#if DEBUG
        // DEBUG-ONLY dev bypass: skip Azure AD B2C entirely and load the app with a minimal
        // in-memory AppState so the UI can be toured without valid tenant credentials.
        // NOTE: data-bound screens (inventory, saved baskets, sales) will be empty because the
        // backend API calls are unauthenticated. Not compiled into Release builds.
        [RelayCommand]
        private void DevSkipLogin()
        {
            var businessId = Guid.NewGuid();
            // Use one identity for both the "current active user" and the logged-in employee so
            // that user-scoped checks (e.g. Settings' admin guard) resolve to a real session.
            var userId = Guid.NewGuid();
            AppState.CurrentActiveUser = new KeyValuePair<Guid, string>(userId, "dev-bypass");
            RepositoryWrapper.SetCurrentUser(userId.ToString());

            var devEmployee = new Employee
            {
                Id = userId,
                Email = "dev@plutus.local",
                FName = "Dev",
                LName = "User",
                Active = true,
                BusinessId = businessId,
                StoreId = 1
            };

            AppState.Business = new Business
            {
                Id = businessId,
                Name = "Dev Business",
                NameAbbr = "DEV",
                VatIN = string.Empty,
                Categories = new List<Category>(),
                Discounts = new List<Discount>(),
                Employees = new List<Employee> { devEmployee },
                Items = new List<Item>(),
                Roles = new List<Role>(),
                Stores = new List<Store>(),
                Taxes = new List<Tax>()
            };
            AppState.Store = new Store { Id = 1, BusinessId = businessId, AdLine1 = "Dev Store" };
            AppState.Till = new Till { Id = AppState.DeviceVendorId, StoreId = 1, CashFloat = decimal.Zero, LastOnline = DateTime.Now };
            AppState.LoggedInEmployees.Add(devEmployee);

            // "Everything enabled": turn on all optional POS features so nothing is gated while
            // touring the app under the dev bypass.
            Core.Settings.CashbackEnabled = true;
            Core.Settings.TryCashDrawer = true;
            Core.Settings.AskForReceipt = true;

            App.Current.MainPage = ServiceHelper.GetService<MainPage>();
        }
#endif
        #endregion

        #region Operations

        private async Task<bool> CreateTill(AuthenticationResult authenticationResult)
        {
            var businesses = await RepositoryWrapper.BusinessRepository.FindAllByCondition(new BusinessParameters { EmployeeObjectId = AppState.CurrentActiveUser.Key });
            if (!businesses.Any())
                return false;

            Business business = default;
            if (businesses.Count() > 1)
            {
                var businessName = await App.Current.MainPage.DisplayActionSheet(Strings.Hmm, Strings.Cancel, null, businesses.Select(b => b.Name).ToArray());
                if (businessName != null || businessName.Equals(Strings.Cancel))
                    return false;

                business = businesses.First(b => b.Name.Equals(businessName));
            }
            else
                business = businesses.First();

            var stores = await RepositoryWrapper.StoreRepository.FindAllByConditionQueryable(new StoreParameters { BusinessId = business.Id });
            if (!stores.Any())
                return false;

            Store store = default;
            if (stores.Count() > 1)
            {
                var storeName = await App.Current.MainPage.DisplayActionSheet(Strings.Hmm, Strings.Cancel, null, stores.Select(s => s.AdLine1).ToArray());
                if (storeName != null || storeName.Equals(Strings.Cancel))
                    return false;

                store = stores.First(s => s.AdLine1.Equals(storeName));
            }
            else
                store = stores.First();

            var till = new Till
            {
                Id = AppState.DeviceVendorId,
                StoreId = store.Id,
                LastOnline = DateTime.Now,
                CashFloat = decimal.Zero
            };

            if (await RepositoryWrapper.TillRepository.Create(till))
                return true;
            return false;
        }

        private async Task<bool> LoadDataIn(AuthenticationResult authenticationResult)
        {
            Business business = default;
            var till = await RepositoryWrapper.TillRepository.FindById(AppState.DeviceVendorId);
            if (till == default)
            {
                if (!await CreateTill(authenticationResult))
                {
                    await App.Current.MainPage.DisplayAlert(Strings.Hmm, Strings.TillCannotBeCreated, Strings.OK);
                    return false;
                }
                till = await RepositoryWrapper.TillRepository.FindById(AppState.DeviceVendorId);
            }

            var store = await RepositoryWrapper.StoreRepository.FindById(till.StoreId);
            if (store != default)
            {
                business = await RepositoryWrapper.BusinessRepository.FindById(store.BusinessId, new BusinessParameters { WithRoles = true });
                if (business != default)
                {
                    var employee = await RepositoryWrapper.EmployeeRepository.FindById(Guid.Parse(authenticationResult.UniqueId), business.Id);
                    if (employee != default)
                    {
                        AppState.LoggedInEmployees.Add(employee);
                        AppState.Business = business;
                        AppState.Store = store;
                        AppState.Till = till;
                        return true;
                    }
                    else
                    {
                        await App.Current.MainPage.DisplayAlert(Strings.Hmm, Strings.UserAccountNotRegisteredWithABusiness, Strings.OK);
                        return false;
                    }
                }
            }
            return false;
        }
        #endregion
    }
}
