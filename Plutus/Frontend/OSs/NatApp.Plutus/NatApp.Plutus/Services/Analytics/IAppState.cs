using System;
using System.Threading.Tasks;

namespace NatApp.Plutus.Services.Analytics
{
    public interface IAppState
    {
        Task Init();
        Guid GetInstallId();
        AppLogLevel GetAppLogLevel();
        void SetAppLogLevel(AppLogLevel level);
    }
}
