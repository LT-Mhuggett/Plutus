using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Plutus.Models;
using Plutus.Helpers;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Syncfusion.DataSource;
using Syncfusion.ListView.XForms;
using Syncfusion.GridCommon.ScrollAxis;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Plutus.Helpers.Extensions;
using ItemTappedEventArgs = Syncfusion.ListView.XForms.ItemTappedEventArgs;

namespace Plutus.Pages.Inventory
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    [SuppressMessage("ReSharper", "RedundantExtendsListEntry")]
    public partial class ViewAllInventoryPage : ContentPage
    {
        public ObservableCollection<ItemModel> Items { get; set; }
        private bool IsAlertShown { get; set; }
        private ScrollAxisBase ScrollRows { get; }
        private int StartLimit { get; set; }
        private int Limit { get; }
        private int TotalItemsInDb { get; set; }
        private IQueryable<ItemModel> Query { get; }

        public ViewAllInventoryPage()
        {
            InitializeComponent();
            
            Query = App.DbContext.GetAllItems()
                .Include(i => i.DisItems)
                    .ThenInclude(di=>di.Discount)
                .Include(i => i.Cat.DisCats)
                    .ThenInclude(dc => dc.Discount)
                .Include(i=>i.Stock);

            ItemList.FooterSize = 20;

            if (ItemList.GetType().GetRuntimeProperties().First(p => p.Name == "VisualContainer")
                .GetValue(ItemList) is VisualContainer visualContainer)
                ScrollRows = visualContainer.GetType().GetRuntimeProperties().First(p => p.Name == "ScrollRows")
                    .GetValue(visualContainer) as ScrollAxisBase;

            Debug.Assert(ScrollRows != null, nameof(ScrollRows) + " != null");
            ScrollRows.Changed += ScrollRows_changed;
            
            StartLimit = 0;

            //Testing use of loading all data
            Limit = 40000;

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
            ItemList.DataSource.Filter = FilterItem;
            ItemList.DataSource.RefreshFilter();
        }

        private bool FilterItem(object obj)
        {
            if (searchBar?.Text == null)
                return true;
            return obj is ItemModel item &&
                   (item.Name.ToLower().Contains(searchBar.Text.ToLower()) ||
                        (item.Brand?.ToLower().Contains(searchBar.Text.ToLower()) ?? false) ||
                        (item.Desc?.ToLower().Contains(searchBar.Text.ToLower()) ?? false));
        }

        private void LoadData()
        {
            TotalItemsInDb = Query.Count();
            var items = Query.OrderBy(item => item.Name).Skip(StartLimit).Take(Limit).ToList();
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
            if (!IsAlertShown && TotalItemsInDb > StartLimit)
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
            var temp = e.ItemData.ToModel<ItemModel>();
            if (!Authorisation.IsAuthorised("Item", "V", App.LastAuthUser))
            {
                await Application.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));

                async void Action()
                {
                    await Navigation.PushModalAsync(new ItemTemplate(temp));
                }

                Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Item", "V", Action);
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
                async void Action() => await Application.Current.MainPage.Navigation.PushAsync(new UpdateItemPage(menuitem));
                Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Item", "M", Action);
                return;
            }
            await Navigation.PushAsync(new UpdateItemPage(menuitem));
        }

        private async void StockUpdate_Clicked(object sender, EventArgs e)
        {
            var menuitem = (ItemModel)((MenuItem)sender).CommandParameter;
            if(!Authorisation.IsAuthorised("Item", "M", App.LastAuthUser))
            {
                async void Action() => await Application.Current.MainPage.Navigation.PushAsync(new StockUpdatePage(menuitem));
                Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Item", "M", Action);
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