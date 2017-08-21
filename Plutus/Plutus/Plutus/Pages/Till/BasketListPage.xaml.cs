using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Plutus.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Till
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class BasketListPage : ContentPage
    {
        public Dictionary<int, ObservableCollection<Basket>> Items { get; set; }

        public BasketListPage()
        {
            InitializeComponent();

            Items = new Dictionary<int, ObservableCollection<Basket>>(MainPage.StoredTrans);
            
            BindingContext = this;
        }

        async void Handle_ItemTapped(object sender, ItemTappedEventArgs e)
        {
            var temp = (KeyValuePair<int, ObservableCollection<Basket>>)e.Item;
            MessagingCenter.Send(new Till.MainPage(),"BasketData", temp);
            await Navigation.PopAsync();
        }
    }
}