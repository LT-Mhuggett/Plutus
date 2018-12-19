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
using Syncfusion.SfChart.XForms;
using System.Collections;

namespace Plutus.Pages.Reports
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class SalesReportsPage : ContentPage
    {
        public ChartController WeeklyChart { get; set; }
        public ChartController DailyChart { get; set; }
        private List<Label> oldTypeLabels = new List<Label>();
        private List<Label> oldValueLabels = new List<Label>();

        public SalesReportsPage()
        {
            InitializeComponent();

            var dateOS = App.DbContext.GetDateOfSales();

            foreach (var DOS in dateOS.OrderBy(d => d.Date))
            {
                var DOSstring = DOS.Date.ToString().Replace(" 12:00:00 AM", "").Replace(" 00:00:00", "");
                if (DateSearch.Items.Contains(DOSstring))
                    continue;
                DateSearch.Items.Add(DOSstring);
            }

            if (DateSearch.Items.Any())
            {
                DateSearch.SelectedIndex = DateSearch.Items.Count - 1;

                WeeklyChart = new ChartController();
                WeeklyChart.SetPrimaryAxis(new DateTimeAxis() { Minimum = dateOS.First(), Maximum = dateOS.Last(), IntervalType = DateTimeIntervalType.Days, Interval = 1 });
                WeeklyChart.SetSecondaryAxis(new NumericalAxis());
                WeeklyChartArea.Children.Add(WeeklyChart.Chart);
            }
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
                var label = new Label { Text = tempPayMeth.Name};
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

                oldTypeLabels.Add(label);
                oldValueLabels.Add(labelAmount);
            }
        }

        private void AddToLayout(StackLayout layout, Label label)
        {
            for(int i = 0; i <= oldTypeLabels.Count-1; i++)
            {
                if (layout.Children.FirstOrDefault(v => v.Equals(oldTypeLabels.ElementAt(i))) != null)
                {
                    layout.Children.Remove(oldTypeLabels.ElementAt(i));
                    layout.Children.Remove(oldValueLabels.ElementAt(i));
                }
            }

            oldTypeLabels = new List<Label>();
            oldValueLabels = new List<Label>();

            layout.Children.Add(label);
        }

        private void DateSearchChange(object sender, EventArgs e)
        {
            InitSales(DateSearch.SelectedItem.ToString());
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            var weeklySales = new List<SaleModel>();

            foreach(var dateText in DateSearch.Items)
            {
                var sales = App.DbContext.GetSales(dateText).ToList();
                weeklySales.AddRange(sales);

            }

            WeeklyChart.AddDataSet("Total", weeklySales, "Total", "DateOfSale");
        }
    }
}