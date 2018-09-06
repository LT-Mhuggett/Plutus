using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Globalization;
using Plutus.Helpers;
using Database.Models;
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

            foreach(var DOS in dateOS.OrderBy(d=>d.Date))
            {
                var DOSstring=DOS.Date.ToString().Replace(" 12:00:00 AM", "").Replace(" 00:00:00", "");
                if (DateSearch.Items.Contains(DOSstring))
                    continue;
                DateSearch.Items.Add(DOSstring);
            }

            if(DateSearch.Items.Any())
                DateSearch.SelectedIndex = 0;
            BindingContext = this;
        }

        private void InitSales(string temp)
        {
            var sales = App.DbContext.GetSales(temp).ToList();
            totalTakins.Text = sales
                .Sum(sale => sale.PaySales.Sum(ps => ps.Amount-ps.Change))
                .ToString(CultureInfo.InvariantCulture);
            foreach (var tempPayMeth in App.DbContext.Get<PaymentMethodModel>())
            {
                var label = new Label {Text = tempPayMeth.Name};
                var labelAmount = new Label();
                var amount = 0.0m;
                foreach (var tempSale in sales)
                {
                    foreach (var tempTakin in tempSale.PaySales)
                    {
                        if (tempPayMeth.Id.Equals(tempTakin.PayMethod.Id))
                        {
                            amount += tempTakin.Amount - tempTakin.Change;
                        }
                    }
                }

                labelAmount.Text = amount.ToString(CultureInfo.InvariantCulture);
                AddToLayout(ContentLayout, label);
                AddToLayout(ContentLayout, labelAmount);
            }
        }

        private void AddToLayout(StackLayout layout, Label label)
        {
            layout.Children.Add(label);
        }

        private void DateSearchChange(object sender, EventArgs e)
        {
            InitSales(DateSearch.SelectedItem.ToString());
        }
    }
}