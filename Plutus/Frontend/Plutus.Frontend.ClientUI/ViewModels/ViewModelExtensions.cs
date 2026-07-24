using Plutus.Frontend.ClientUI.ViewModels.PopupViewModels;

namespace Plutus.Frontend.ClientUI.ViewModels
{
    public static class ViewModelsExtensions
    {
        public static MauiAppBuilder ConfigureViewModels(this MauiAppBuilder builder)
        {
            #region ViewModels
            builder.Services.AddTransient<LoginViewModel>();
            builder.Services.AddSingleton<MainTill.TillViewModel>();
            builder.Services.AddSingleton<MainTill.Inventory.InventoryViewModel>();
            builder.Services.AddTransient<MainTill.Inventory.ViewAllInventoryViewModel>();
            builder.Services.AddTransient<MainTill.Inventory.AddEditInventoryItemViewModel>();
            builder.Services.AddSingleton<MainTill.Statistics.StatisticsViewModel>();
            builder.Services.AddTransient<MainTill.Statistics.SalesReportViewModel>();
            builder.Services.AddTransient<MainTill.Settings.SettingsViewModel>();
            #endregion

            #region Popup ViewModels
            builder.Services.AddSingleton<LoadingIndicatorViewModel>();
            builder.Services.AddTransient<AuthorisationViewModel>();
            builder.Services.AddTransient<AdjustItemViewModel>();
            builder.Services.AddTransient<ReturnItemViewModel>();
            builder.Services.AddTransient<CategoryCreateViewModel>();
            builder.Services.AddTransient<MoniesInputViewModel>();
            builder.Services.AddTransient<AlterationViewModel>();
            #endregion

            return builder;
        }
    }
}
