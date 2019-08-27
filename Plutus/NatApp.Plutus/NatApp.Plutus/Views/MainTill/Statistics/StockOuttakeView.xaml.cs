using NatApp.Plutus.ViewModels.MainTill.Statistics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace NatApp.Plutus.Views.MainTill.Statistics
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class StockOuttakeView : ContentPage
    {
        public StockOuttakeView()
        {
            InitializeComponent();
        }
        /*
        private void ListView_ItemSelected(object sender, SelectedItemChangedEventArgs e)
        {
            if (sunChart != null) return;
        }*/
    }
}