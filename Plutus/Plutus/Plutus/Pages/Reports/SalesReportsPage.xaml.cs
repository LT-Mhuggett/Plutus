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
        private Dictionary<string, Label> PayMethLabels = new Dictionary<string, Label>();

        public SalesReportsPage()
        {
            InitializeComponent();

            var dateOS = App.DbContext.GetDateOfSales();

            var payMethods = App.DbContext.Get<PaymentMethodModel>().Select(pM=>pM.Name).ToList();

            foreach(var payMethod in payMethods)
            {
                ContentLayout.Children.Add(new Label() { Text = payMethod});
                PayMethLabels.Add(payMethod, new Label() { Text = "0.00" });
                ContentLayout.Children.Add(PayMethLabels.First(pML=>pML.Key.Equals(payMethod)).Value);
            }

            ContentLayout.Children.Add(new Label() { Text = "Total" });
            PayMethLabels.Add("Total", new Label() { Text = "0.00" });
            ContentLayout.Children.Add(PayMethLabels.First(pML => pML.Key.Equals("Total")).Value);

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
                /*
                WeeklyChart = new ChartController();
                WeeklyChart.SetPrimaryAxis(new DateTimeAxis() { Minimum = dateOS.First(), Maximum = dateOS.Last(), IntervalType = DateTimeIntervalType.Days, Interval = 1 });
                WeeklyChart.SetSecondaryAxis(new NumericalAxis());
                WeeklyChartArea.Children.Add(WeeklyChart.Chart);*/
            }
            BindingContext = this;
        }

        private void InitSales(string temp)
        {
            var sales = App.DbContext.GetSales(temp).ToList();
            foreach (var tempPayMeth in App.DbContext.Get<PaymentMethodModel>())
            {
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

                if(PayMethLabels.TryGetValue(tempPayMeth.Name, out Label payMethLabel)){
                    payMethLabel.Text = amount.ToString();
                }
            }

            if(PayMethLabels.TryGetValue("Total", out Label totalLabel))
            {
                totalLabel.Text = sales
                    .Sum(sale => sale.PaySales.Sum(ps => ps.Amount - ps.Change))
                    .ToString(CultureInfo.InvariantCulture);
            }
        }

        private void DateSearchChange(object sender, EventArgs e)
        {
            InitSales(DateSearch.SelectedItem.ToString());
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            /*
            var weeklySales = new List<SaleModel>();

            foreach(var dateText in DateSearch.Items)
            {
                var sales = App.DbContext.GetSales(dateText).ToList();
                weeklySales.AddRange(sales);

            }

            WeeklyChart.AddDataSet("Total", weeklySales, "Total", "DateOfSale");*/
        }
    }
}