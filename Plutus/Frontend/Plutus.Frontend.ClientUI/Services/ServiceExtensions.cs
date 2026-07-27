using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Frontend.ClientUI.Services.Authentication;
using Plutus.Frontend.ClientUI.Services.Loading;
using Plutus.Frontend.ClientUI.Services.PopupSize;
using Plutus.Frontend.ClientUI.Services.PosHandeling;
using Plutus.Frontend.ClientUI.Services.Repository;
using Plutus.Frontend.ClientUI.Services.Repository.Contracts;

namespace Plutus.Frontend.ClientUI.Services
{
    public static class ServiceExtensions
    {
        public static MauiAppBuilder ConfigureServices(this MauiAppBuilder builder)
        {
            builder.Services.AddSingleton<ILogger, Logger>();
            builder.Services.AddSingleton<IPopupSize, PopupSize.PopupSize>();
            builder.Services.AddSingleton<IRepositoryWrapper, RepositoryWrapper>();
            builder.Services.AddSingleton<LoadingViewService>();
            builder.Services.AddTransient<IAuthService, AuthService>();
            builder.Services.AddTransient<PosPrinterManager>();
            return builder;
        }
    }
}
