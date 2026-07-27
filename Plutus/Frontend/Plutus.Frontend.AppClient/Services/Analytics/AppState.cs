using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

namespace Plutus.Frontend.AppClient.Services.Analytics
{
    public class AppState : IAppState
    {
        private const string InstallIdPreferenceKey = "Plutus.InstallId";

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

        public Task Init()
        {
            var stored = Preferences.Default.Get(InstallIdPreferenceKey, string.Empty);
            if (!Guid.TryParse(stored, out _installId))
            {
                _installId = Guid.NewGuid();
                Preferences.Default.Set(InstallIdPreferenceKey, _installId.ToString());
            }

            return Task.CompletedTask;
        }

        public void SetAppLogLevel(AppLogLevel level)
        {
            _logLevel = level;
        }

        public bool IsEmulatorOrSimulator()
        {
            return DeviceInfo.DeviceType == DeviceType.Virtual;
        }
    }
}
