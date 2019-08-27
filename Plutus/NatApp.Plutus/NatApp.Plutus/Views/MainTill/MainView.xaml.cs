using Plugin.Iconize;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace NatApp.Plutus.Views.MainTill
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class MainView : IconTabbedPage
    {
        public MainView()
        {
            InitializeComponent();

            //Set Pages
            Children.Add(new Till.TillView());
            Children.Add(new Inventory.InventoryView());
            Children.Add(new Statistics.StatisticsView());
            if (Device.Idiom == TargetIdiom.Desktop)
            {
                Children.Add(new StoreOptions.StoreOptionsView());
            }
            Children.Add(new Settings.SettingsView());
            App.SetLoading(false);
        }
    }
}