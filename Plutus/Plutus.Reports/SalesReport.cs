using System;
using System.Collections.Generic;
using Plutus.Entities.Models;


namespace Plutus.Reports
{
    public class SalesReport
    {
        #region Private Fields
        //private ChartSeriesCollection _series;
        //private SelectionRange _selectionRange;
        private decimal SalesTotalExTax ;
        private decimal _totalTax;
        private decimal SalesTotal;
        private DateTime _calMinDate;
        private DateTime _calMaxDate;
        /// <summary>
        /// Is view Table view of Graph view
        /// </summary>
        /// <remarks>
        /// True = Table View; False = Graph View
        /// </remarks>
        private bool _tableView;

        //private Layout<View> _gridView;
        #endregion

        public String SalesDataLoading(DateTime startDate, DateTime endDate)
        {
            SalesTotalExTax = 0m;
            SalesTotal = 0m;
            return "";
            //var salesData = new List<Tuple<string, List<SalesDataByPayMethod>>>();
            /*for (int days = 0;
                days <= Math.Abs((startDate - endDate).TotalDays);
                days++)
            {
                var date = startDate.AddDays(days);
                var payMethods = db.Get<PaymentMethodModel>().Select(pM => pM.Name).ToList();

                foreach (var payMethod in payMethods)
                {
                    var paySales = db.Get<PaymentMethod_SaleModel>()
                    .Include(ps => ps.PayMethod)
                    .Include(ps => ps.Sale)
                    .ThenInclude(s => s.Transactions)
                    .Where(ps =>
                        ps.Sale.DateOfSale.Date.Equals(date.Date) &&
                        ps.PayMethod.Name.Equals(payMethod)).ToList();

                    var exVat = paySales.Sum(ps => ps.Sale.TotalExTax * ((ps.Amount - ps.Change) / ps.Sale.Total)).Normalize();

                    var datumExVat = new SalesDataByPayMethod
                    {
                        PayMethod = payMethod + " " + "ExTax".Translate(),
                        Date = $"{date.ToShortDateString()}\n{date.DayOfWeek}",
                        Monies = exVat,
                        Day = date.DayOfWeek.ToString()
                    };

                    var datumIncVat = new SalesDataByPayMethod
                    {
                        PayMethod = payMethod,
                        Date = $"{date.ToShortDateString()}\n{date.DayOfWeek}",
                        Monies = paySales.Sum(ps => ps.Amount - ps.Change),
                        Day = date.DayOfWeek.ToString()
                    };

                    if (salesData.Any(wSD => wSD.Item1.Equals(payMethod)))
                    {
                        salesData.First(wSD => wSD.Item1.Equals(payMethod + " " + "ExTax".Translate()))
                            .Item2.Add(datumExVat);
                        salesData.First(wSD => wSD.Item1.Equals(payMethod))
                            .Item2.Add(datumIncVat);
                    }
                    else
                    {
                        salesData.Add(
                        Tuple.Create(
                            payMethod + " " + "ExTax".Translate(),
                            new List<SalesDataByPayMethod> { datumExVat }));
                        salesData.Add(
                        Tuple.Create(
                            payMethod,
                            new List<SalesDataByPayMethod> { datumIncVat }));
                    }

                    SalesTotalExTax += exVat;
                    SalesTotal += paySales.Sum(ps => ps.Amount - ps.Change);
                }

                TotalTax = (SalesTotal - SalesTotalExTax);
            }
            CreateSeries(salesData);*/
        }

        /*private void CreateSeries(List<Tuple<string, List<SalesDataByPayMethod>>> salesData)
        {
            Series.Clear();

            foreach (var salesDatum in salesData)
            {
                Series.Add(new ColumnSeries
                {
                    Label = salesDatum.Item1,
                    ItemsSource = salesDatum.Item2,
                    XBindingPath = "Date",
                    YBindingPath = "Monies",
                    EnableAnimation = true,
                    DataMarker = new ChartDataMarker
                    {
                        LabelStyle = new DataMarkerLabelStyle
                        {
                            LabelPosition = DataMarkerLabelPosition.Auto
                        }
                    },
                    EnableTooltip = true,
                    TooltipTemplate = new DataTemplate(() =>
                    {
                        var stack = new StackLayout { Orientation = StackOrientation.Vertical };
                        var firstInner = new StackLayout { Orientation = StackOrientation.Horizontal };
                        var labelXTitle = new Label { Text = "Day:", TextColor = Color.White };
                        var labelX = new Label { TextColor = Color.White };
                        labelX.SetBinding(Label.TextProperty, "Day");
                        firstInner.Children.Add(labelXTitle);
                        firstInner.Children.Add(labelX);

                        var secondInner = new StackLayout { Orientation = StackOrientation.Horizontal };
                        var labelYTitle = new Label { Text = "Monies:", TextColor = Color.White };
                        var labelY = new Label { TextColor = Color.White };
                        labelY.SetBinding(Label.TextProperty, "Monies");
                        secondInner.Children.Add(labelYTitle);
                        secondInner.Children.Add(labelY);

                        stack.Children.Add(firstInner);
                        stack.Children.Add(secondInner);
                        return stack;
                    })
                });
            }
        }*/

        public class SalesDataByPayMethod
        {
            public string PayMethod { get; set; }
            public string Day { get; set; }
            public string Date { get; set; }
            public decimal Monies { get; set; }
        }
    }


}
