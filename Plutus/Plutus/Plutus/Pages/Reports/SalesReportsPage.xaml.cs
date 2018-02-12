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
        public SalesReportsPage()
        {
            InitializeComponent();

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

        private void InitSales(string temp)
        {
            var sales = App.DbContext.GetSales(temp).ToList();
            totalTakins.Text = sales
                .Sum(sale => sale.PaySales.Sum(ps => ps.Amount))
                .ToString(CultureInfo.InvariantCulture);
            cashTakins.Text = sales
                .Sum(sale => sale.PaySales.Where(ps => ps.PayMethod.Name.Equals("Cash")).Sum(ps => ps.Amount))
                .ToString(CultureInfo.InvariantCulture);
            cardTakins.Text = sales
                .Sum(sale => sale.PaySales.Where(ps => ps.PayMethod.Name.Equals("Card")).Sum(ps => ps.Amount))
                .ToString(CultureInfo.InvariantCulture);
        }

        private void DateSearchChange(object sender, EventArgs e)
        {
            InitSales(DateSearch.SelectedItem.ToString());
        }
    }
}