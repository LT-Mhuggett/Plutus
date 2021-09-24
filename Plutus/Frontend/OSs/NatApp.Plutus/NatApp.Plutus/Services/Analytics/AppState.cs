using Microsoft.AppCenter;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xamarin.Essentials;

namespace NatApp.Plutus.Services.Analytics
{
    public class AppState : IAppState
    {
        private Guid _installId = Guid.Empty;
        private AppLogLevel _logLevel;

        public AppLogLevel GetAppLogLevel()
        {
            return _logLevel;
        }

        public Guid GetInstallId()
        {
            return _installId;
        }

        public async Task Init()
        {
            if (await CheckAppCenter())
            {
                Guid? installId = await AppCenter.GetInstallIdAsync();
                if (installId != null)
                {
                    _installId = (Guid)installId;
                }
            }
        }

        public void SetAppLogLevel(AppLogLevel level)
        {
            _logLevel = level;
        }

        private async Task<bool> CheckAppCenter()
        {
            var retValue = true;

            try
            {
                if (Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    bool isAnalyticsEnabled = await Microsoft.AppCenter.Analytics.Analytics.IsEnabledAsync();
                    if (isAnalyticsEnabled)
                    {
                        Debug.WriteLine($"[{this.GetType().ToString()}] Warning: AppCenter Analytics is NOT enabled.");
                        retValue = false;
                    }

                    bool isCrashEnabled = await Microsoft.AppCenter.Crashes.Crashes.IsEnabledAsync();
                    if (isCrashEnabled)
                    {
                        Debug.WriteLine(
                            $"[{this.GetType().ToString()}] Warning: AppCenter Crash Reporting is NOT enabled.");
                        retValue = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{this.GetType().ToString()}] Exception while checking if AppCenter is enabled {ex.Message} {ex.StackTrace}");
                retValue = false;
            }

            return retValue;
        }
    }
}
