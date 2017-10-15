using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Globalization;
using Plutus.Helpers;
using Plutus.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using System.Collections.Generic;

namespace Plutus.Pages.Reports
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class SalesReportsPage : ContentPage
    {
        public ObservableCollection<SaleModel> Sales { get; set; }

        public SalesReportsPage()
        {
            InitializeComponent();

            Sales =new ObservableCollection<SaleModel>();

            var dateOS = App.DbContext.GetDateOfSales();

            foreach(var DOS in dateOS)
            {
                var DOSstring=DOS.Date.ToString().Replace(" 12:00:00 AM", "").Replace(" 00:00:00", "");
                if (DateSearch.Items.Contains(DOSstring))
                    continue;
                DateSearch.Items.Add(DOSstring);
            }
            
            BindingContext = this;
        }

        void Handle_ItemTapped(object sender, SelectedItemChangedEventArgs e)
        {
            if (e.SelectedItem == null)
                return;
            //Deselect Item
            ((ListView)sender).SelectedItem = null;
        }

        private void InitSales(string temp)
        {
            Sales.Clear();
            var sales = App.DbContext.GetSales(temp).ToList();
            foreach(var sale in sales)
            {
                Sales.Add(sale);
            }
        }

        private void EmpSearchCompleted(object sender, EventArgs e)
        {
            InitSales(EmpSearch.Text);
        }

        private void DateSearchChange(object sender, EventArgs e)
        {
            InitSales(DateSearch.SelectedItem.ToString());
        }
    }
}