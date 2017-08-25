using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Plutus.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Inventory
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ViewAllInventoryPage : ContentPage
    {
        public ObservableCollection<ItemModel> Items { get; set; }

        public ViewAllInventoryPage()
        {
            InitializeComponent();

            Items = new ObservableCollection<ItemModel>();

            BindingContext = this;

            Device.BeginInvokeOnMainThread(() =>
            {
                InitItems();
            });
        }

        async void Handle_ItemTapped(object sender, SelectedItemChangedEventArgs e)
        {
            if (e.SelectedItem == null)
                return;

            await DisplayAlert("Item Tapped", "An item was tapped.", "OK");

            //Deselect Item
            ((ListView)sender).SelectedItem = null;
        }

        void InitItems()
        {
            var items = App.DbContext.GetAllItems();
            foreach(var item in items)
            {
                Items.Add(item);
            }
        }
    }
}