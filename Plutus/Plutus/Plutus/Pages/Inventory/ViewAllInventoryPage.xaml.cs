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
        public ObservableCollection<InventGroup> Items { get; set; }

        public ViewAllInventoryPage()
        {
            InitializeComponent();

            Items = new ObservableCollection<InventGroup>();

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
            foreach (var item in items.OrderBy(i=>i.Name))
            {
                if (Items.Count == 0)
                {
                    var title = item.Name.FirstOrDefault().ToString();
                    InventGroup G = new InventGroup(title, title);
                    Items.Add(G);
                    G.Add(item);
                }
                else
                {
                    foreach (var tempItem in Items)
                    {
                        if (tempItem.Title == item.Name.FirstOrDefault().ToString())
                        {
                            tempItem.Add(item);
                        }
                        else
                        {
                            var title = item.Name.FirstOrDefault().ToString();
                            InventGroup G = new InventGroup(title, title);
                            Items.Add(G);
                            G.Add(item);
                        }
                    }
                }
            }
            Items = new ObservableCollection<InventGroup>(Items);
            BindingContext = this;
        }
    }

    public class InventGroup : ObservableCollection<ItemModel>
    {
        public string Title { get; set; }
        public string ShortName { get; set; }
        public InventGroup(string title, string sName)
        {
            Title = title;
            ShortName = sName;
        }
    }
}