using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Core.AppSettings;
using Plutus.Frontend.ClientUI.Core.Extensions;
using Plutus.Frontend.ClientUI.Resources.I18N_L10N;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Frontend.ClientUI.Services.Loading;
using Plutus.Frontend.ClientUI.Services.Repository.Contracts;
using Microsoft.Extensions.Configuration;

namespace Plutus.Frontend.ClientUI.ViewModels.MainTill.Statistics
{
    public partial class SalesReportViewModel : BaseViewModel
    {
        private AppSettings _appSettings;
        [ObservableProperty]
        public DateTime _startDate;
        [ObservableProperty]
        public DateTime _endDate;

        public SalesReportViewModel(ILogger logger, IAppState appState, LoadingViewService loadingViewService, IRepositoryWrapper repositoryWrapper, IConfiguration configuration) : base(logger, appState, loadingViewService, repositoryWrapper)
        {
            StartDate = DateTime.Now.StartOfWeek().AddMonths(-1);
            EndDate = DateTime.Now.StartOfWeek();
            Title = Strings.SalesReports;
            Icon = "\uf080";
            _appSettings = configuration.GetRequiredSection("AppSettings").Get<AppSettings>();
        }

        [RelayCommand]
        private async void GenerateReport()
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
#if DEBUG
                using var client = new HttpClient(GetInsecureHandler())
                {
                    BaseAddress = new Uri($"{_appSettings.DBServiceURL}/api/")
                };
#else
                using var client = new HttpClient
                {
                    BaseAddress = new Uri($"{_appSettings.DBServiceURL}/api/")
                };
#endif
                var message = new HttpRequestMessage(HttpMethod.Get, $"Sale/SaleReport?minDate={StartDate.Date}&maxDate={EndDate.Date}");
                message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("bearer", AppState.CurrentActiveUser.Value);
                var response = await client.SendAsync(message);
                if(response.IsSuccessStatusCode)
                {
                    using var resultStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = File.Create(FileSystem.Current.CacheDirectory + $"SalesReport--{StartDate.Date}-{EndDate.Date}");
                    resultStream.CopyTo(fileStream);
                    fileStream.Close();
                    resultStream.Close();

                    await Share.Default.RequestAsync(new ShareFileRequest
                    {
                        Title = "Share Sales Report",
                        File = new ShareFile(Path.Combine(FileSystem.Current.CacheDirectory, $"SalesReport--{StartDate.Date}-{EndDate.Date}"))
                    });
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

#if DEBUG
        private HttpClientHandler GetInsecureHandler()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => {
                    if (cert.Issuer.Equals("CN=localhost"))
                        return true;
                    return errors == System.Net.Security.SslPolicyErrors.None;
                }
            };

            return handler;
        }
#endif
    }
}
