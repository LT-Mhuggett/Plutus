using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Database.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Till
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class BasketListPage : ContentPage
    {
        public Dictionary<int, Tuple<string, ObservableCollection<ItemModel>>> Items { get; set; }

        /// <summary>
        /// Basic constructor for BasketListPage
        /// initalises the Items Dictionary
        /// and the binding context
        /// </summary>
        public BasketListPage()
        {
            InitializeComponent();

            Items = new Dictionary<int, Tuple<string, ObservableCollection<ItemModel>>>(MainPage.StoredTrans);
            
            BindingContext = this;
        }

        /// <summary>
        /// Get the item that was tapped and send it to MainPage.GetchSavedBasket
        /// then pop current page
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        async void Handle_ItemTapped(object sender, ItemTappedEventArgs e)
        {
            var temp = (KeyValuePair<int, Tuple<string, ObservableCollection<ItemModel>>>)e.Item;
            MainPage.FetchSavedBasket(temp, MainPage.Instance);
            await Navigation.PopAsync();
        }
    }
}