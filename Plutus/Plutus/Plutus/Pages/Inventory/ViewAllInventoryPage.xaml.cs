using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Plutus.Models;
using Plutus.Helpers;
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
            ItemModel temp = (ItemModel)((ListView)sender).SelectedItem;
            if (!Authorisation.IsAuthorised("Item", "V", App.LastAuthUser))
            {
                await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
                Action action = async () =>
                {
                    await Navigation.PushModalAsync(new ItemTemplate(temp));
                    ((ListView)sender).SelectedItem = null;
                };
                Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Item", "V", action);
            }
            else
            {
                await Navigation.PushModalAsync(new ItemTemplate(temp));
                ((ListView)sender).SelectedItem = null;
            }
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

        private async void UpdateItem_Clicked(object sender, EventArgs e)
        {
            var menuitem = (ItemModel)((MenuItem)sender).CommandParameter;
            if (!Authorisation.IsAuthorised("Item", "M", App.LastAuthUser)){
                Action action = async () => await App.Current.MainPage.Navigation.PushAsync(new UpdateItemPage(menuitem));
                Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Item", "M", action);
                return;
            }
            await Navigation.PushAsync(new UpdateItemPage(menuitem));
        }

        private void CancelEmpCheck_Clicked(object sender, EventArgs e)
        {
            EId.Text = null;
            VerifyId.IsVisible = false;
            MPage.IsEnabled = true;
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