using System;
using System.Threading.Tasks;

namespace Plutus.Frontend.AppClient.Services.Analytics
{
    public interface IAppState
    {
        Task Init();
        Guid GetInstallId();
        AppLogLevel GetAppLogLevel();
        void SetAppLogLevel(AppLogLevel level);
        bool IsEmulatorOrSimulator();
    }
}
