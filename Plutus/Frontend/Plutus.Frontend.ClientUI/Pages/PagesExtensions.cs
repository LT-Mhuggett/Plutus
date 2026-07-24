using Plutus.Frontend.ClientUI.Pages.PopupViews;

namespace Plutus.Frontend.ClientUI.Pages
{
    public static class PagesExtensions
    {
        public static MauiAppBuilder ConfigurePages(this MauiAppBuilder builder)
        {
            #region Pages
            builder.Services.AddTransient<LoginPage>();
            builder.Services.AddTransient<MainTill.MainPage>();
            builder.Services.AddSingleton<MainTill.TillPage>();
            builder.Services.AddSingleton<MainTill.Inventory.InventoryPage>();
            builder.Services.AddTransient<MainTill.Inventory.ViewAllInventoryPage>();
            builder.Services.AddTransient<MainTill.Inventory.AddEditInventoryItemPage>();
            builder.Services.AddSingleton<MainTill.Statistics.StatisticsPage>();
            builder.Services.AddTransient<MainTill.Statistics.SalesReportPage>();
            builder.Services.AddTransient<MainTill.Statistics.StockOuttakePage>();
            builder.Services.AddTransient<MainTill.Settings.SettingsPage>();
            #endregion

            #region Popup Pages
            builder.Services.AddSingleton<LoadingIndicatorPage>();
            builder.Services.AddTransient<AuthorisationPage>();
            builder.Services.AddTransient<AdjustItemPage>();
            builder.Services.AddTransient<ReturnItemPage>();
            builder.Services.AddTransient<CategoryCreatePage>();
            builder.Services.AddTransient<MoniesInputPage>();
            builder.Services.AddTransient<AlterationPage>();
            #endregion

            return builder;
        }
    }
}
