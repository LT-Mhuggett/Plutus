using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Maui.LifecycleEvents;
using Plutus.Entities;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Core.AppSettings;
using Plutus.Frontend.ClientUI.Pages;
using Plutus.Frontend.ClientUI.Services;
using Plutus.Frontend.ClientUI.ViewModels;

namespace Plutus.Frontend.ClientUI
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                //.ConfigureEssentials()
                .ConfigureAppSettings()
                .RegisterEssentials()
                .ConfigureServices()
                .ConfigurePages()
                .ConfigureViewModels()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("FontAwesome5_Brands_Regular_400.otf", "FABrandsRegular");
                    fonts.AddFont("FontAwesome5_Free_Regular_400.otf", "FARegular");
                    fonts.AddFont("FontAwesome5_Free_Solid_900.otf", "FASolid");
                })
                .UseMauiCommunityToolkit()
                .UseMauiCommunityToolkitMarkup();

            builder.ConfigureLifecycleEvents(lifecycle =>
            {
#if WINDOWS
                lifecycle.AddWindows(windows => windows.OnWindowCreated((del) => {
                    del.ExtendsContentIntoTitleBar = true;
                }));
#endif
            });
            return builder.Build();

            /*var appState1 = app.Services.GetService<IAppState>();
            var appState2 = app.Services.GetService<IAppState>();
            var appState5 = ServiceHelper.GetService<IAppState>();
            appState1.Init();
            var appState3 = app.Services.GetService<IAppState>();
            var appState4 = ServiceHelper.GetService<IAppState>();
            return app;*/
        }

        private static MauiAppBuilder RegisterEssentials(this MauiAppBuilder builder)
        {
            builder.Services.AddSingleton<IAppState, AppState>();
            builder.Services.AddSingleton(DeviceInfo.Current);
            builder.Services.AddSingleton(DeviceDisplay.Current);
            builder.Services.AddDbContext<SqliteDbContext>(options => {
                options.UseSqlite(@$"Data Source={Path.Combine(FileSystem.AppDataDirectory, "Database.db")}");
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
                });
            return builder;
        }
    }
}