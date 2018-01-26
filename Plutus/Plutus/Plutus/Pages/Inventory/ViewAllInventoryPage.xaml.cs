using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Plutus.Models;
using Plutus.Helpers;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Syncfusion.DataSource;
using Syncfusion.ListView.XForms;
using Syncfusion.GridCommon.ScrollAxis;
using System.Reflection;
using ItemTappedEventArgs = Syncfusion.ListView.XForms.ItemTappedEventArgs;

namespace Plutus.Pages.Inventory
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ViewAllInventoryPage : ContentPage
    {
        public ObservableCollection<ItemModel> Items { get; set; }
        private bool IsAlertShown { get; set; }
        private ScrollAxisBase ScrollRows { get; set; }
        private int StartLimit { get; set; }
        private int Limit { get; set; }
        private int TotalItemsInDB { get; set; }

        public ViewAllInventoryPage()
        {
            InitializeComponent();

            ItemList.FooterSize = 40;

            VisualContainer visualContainer = ItemList.GetType().GetRuntimeProperties().First(p => p.Name == "VisualContainer")
                .GetValue(ItemList) as VisualContainer;

            ScrollRows = visualContainer.GetType().GetRuntimeProperties().First(p => p.Name == "ScrollRows")
                .GetValue(visualContainer) as ScrollAxisBase;

            ScrollRows.Changed += ScrollRows_changed;

            Items = new ObservableCollection<ItemModel>();

            StartLimit = 0;

            Limit = 20;

            LoadData();

            ItemList.ItemsSource = Items;

            ItemList.DataSource.GroupDescriptors.Add(new GroupDescriptor()
            {
                PropertyName = "GroupKey"
            });
        }

        private void LoadData()
        {
            TotalItemsInDB = App.DbContext.GetAllItems().Count();

            var items = App.DbContext.GetAllItems().OrderBy(item => item.Name).Skip(StartLimit).Take(Limit);
            foreach (var item in items)
            {
                item.GroupKey = item.Name[0];
                Items.Add(item);
            }
            StartLimit += Limit;
        }

        private void ScrollRows_changed(object sender, ScrollChangedEventArgs e)
        {
            var lastIndex = ScrollRows.LastBodyVisibleLineIndex;

            var header = (ItemList.HeaderTemplate != null && !ItemList.IsStickyHeader) ? 1 : 0;

            var footer = (ItemList.FooterTemplate != null && !ItemList.IsStickyFooter) ? 1 : 0;
            var totalItems = ItemList.DataSource.DisplayItems.Count + header + footer;

            if(lastIndex == totalItems - 1)
            {
                if (!IsAlertShown && TotalItemsInDB > StartLimit)
                {
                    IsAlertShown = !IsAlertShown;
                    LoadData();
                }
                else
                {
                    IsAlertShown = !IsAlertShown;
                }
            }
        }

        private async void Handle_ItemTapped(object sender, ItemTappedEventArgs e)
        {
            if (e.ItemData == null)
                return;
            ItemModel temp = Conversions.ToModel<ItemModel>(e.ItemData);
            if (!Authorisation.IsAuthorised("Item", "V", App.LastAuthUser))
            {
                await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
                Action action = async () =>
                {
                    await Navigation.PushModalAsync(new ItemTemplate(temp));
                };
                Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Item", "V", action);
            }
            else
            {
                await Navigation.PushModalAsync(new ItemTemplate(temp));
            }

        }

        private async void UpdateItem_Clicked(object sender, EventArgs e)
        {
            var menuitem = (ItemModel)((MenuItem)sender).CommandParameter;
            if (!Authorisation.IsAuthorised("Item", "M", App.LastAuthUser))
            {
                Action action = async () => await App.Current.MainPage.Navigation.PushAsync(new UpdateItemPage(menuitem));
                Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Item", "M", action);
                return;
            }
            await Navigation.PushAsync(new UpdateItemPage(menuitem));
        }

        private async void StockUpdate_Clicked(object sender, EventArgs e)
        {
            var menuitem = (ItemModel)((MenuItem)sender).CommandParameter;
            if(!Authorisation.IsAuthorised("Item", "M", App.LastAuthUser))
            {
                Action action = async () => await App.Current.MainPage.Navigation.PushAsync(new StockUpdatePage(menuitem));
                Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Item", "M", action);
                return;
            }
            await Navigation.PushAsync(new StockUpdatePage(menuitem));
        }

        private void CancelEmpCheck_Clicked(object sender, EventArgs e)
        {
            EId.Text = null;
            VerifyId.IsVisible = false;
            MPage.IsEnabled = true;
        }
    }
}