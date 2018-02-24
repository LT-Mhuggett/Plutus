using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
using Plutus.Helpers.Extensions;
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
        private IQueryable<ItemModel> query { get; set; }

        public ViewAllInventoryPage()
        {
            InitializeComponent();
            
            query = App.DbContext.GetAllItems();

            ItemList.FooterSize = 20;

            if (ItemList.GetType().GetRuntimeProperties().First(p => p.Name == "VisualContainer")
                .GetValue(ItemList) is VisualContainer visualContainer)
                ScrollRows = visualContainer.GetType().GetRuntimeProperties().First(p => p.Name == "ScrollRows")
                    .GetValue(visualContainer) as ScrollAxisBase;

            Debug.Assert(ScrollRows != null, nameof(ScrollRows) + " != null");
            ScrollRows.Changed += ScrollRows_changed;
            
            StartLimit = 0;
            Limit = 40;

            SetItems();

            LoadData();
            
            ItemList.DataSource.GroupDescriptors.Add(new GroupDescriptor()
            {
                PropertyName = "GroupKey"
            });
        }

        private void SetItems()
        {
            StartLimit = 0;
            Items = new ObservableCollection<ItemModel>();
            
            ItemList.ItemsSource = Items;
        }

        private void searchBar_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ItemList.DataSource == null) return;
            this.ItemList.DataSource.Filter = FilterItem;
            this.ItemList.DataSource.RefreshFilter();
        }

        private bool FilterItem(object obj)
        {
            if (searchBar?.Text == null)
                return true;
            var item = obj as ItemModel;
            return item.Name.ToLower().Contains(searchBar.Text.ToLower()) ||
                   item.Brand.ToLower().Contains(searchBar.Text.ToLower()) ||
                   (item.Desc?.ToLower().Contains(searchBar.Text.ToLower()) ?? false);
        }

        private void LoadData()
        {
            TotalItemsInDB = query.Count();
            var items = query.OrderBy(item => item.Name).Skip(StartLimit).Take(Limit).ToList();
            foreach (var item in items)
            {
                item.GroupKey = item.Name.ToUpper()[0];
                Items.Add(item);
            }

            StartLimit += Limit;
        }

        private void ScrollRows_changed(object sender, ScrollChangedEventArgs e)
        {
            var lastIndex = ScrollRows.LastBodyVisibleLineIndex;

            var header = (ItemList.HeaderTemplate != null && !ItemList.IsStickyHeader) ? 1 : 0;
            var groupHeader = (ItemList.GroupHeaderTemplate != null && !ItemList.IsStickyGroupHeader) ? 1 : 0;
            var footer = (ItemList.FooterTemplate != null && !ItemList.IsStickyFooter) ? 1 : 0;

            var totalItems = ItemList.DataSource.DisplayItems.Count - 1 + header + footer + groupHeader;

            if (lastIndex != totalItems) return;
            if (!IsAlertShown && TotalItemsInDB > StartLimit)
            {
                IsAlertShown = !IsAlertShown;
                LoadData();
            }
            else
            {
                IsAlertShown = false;
            }
        }

        private async void Handle_ItemTapped(object sender, ItemTappedEventArgs e)
        {
            if (e.ItemData.ToModel<ItemModel>() == null)
                return;
            ItemModel temp = e.ItemData.ToModel<ItemModel>();
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
            e.Handled = true;
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
        /*
        private void searchBar_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (searchBar.Text != null)
            {
                query = App.DbContext.GetAllItems().Where(item =>
                    item.Name.Contains(searchBar.Text) || item.Brand.Contains(searchBar.Text));
            }
            else
                query = App.DbContext.GetAllItems();
            SetItems();
            LoadData();
        }*/
    }
}