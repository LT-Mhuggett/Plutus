using Database.Models;
using Microsoft.EntityFrameworkCore;
using NatApp.Plutus.Helpers.Extensions;
using NatApp.Plutus.Services.IOHandeling;
using Syncfusion.SfCalendar.XForms;
using Syncfusion.SfChart.XForms;
using Syncfusion.XlsIO;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace NatApp.Plutus.ViewModels.MainTill.Statistics
{
    public class SalesReportsViewModel : BaseViewModel
    {
        #region Private Fields
        private ChartSeriesCollection _series;
        private SelectionRange _selectionRange;
        private decimal _salesTotalExTax;
        private decimal _totalTax;
        private decimal _salesTotal;
        private DateTime _calMinDate;
        private DateTime _calMaxDate;
        /// <summary>
        /// Is view Table view of Graph view
        /// </summary>
        /// <remarks>
        /// True = Table View; False = Graph View
        /// </remarks>
        private bool _tableView;
        private Layout<View> _gridView;
        #endregion

        #region Properties
        public ChartSeriesCollection Series
        {
            get => _series;
            set => SetProperty(ref _series, value);
        }
        public SelectionRange SelectionRange
        {
            get => _selectionRange;
            set => SetProperty(ref _selectionRange, value, onChanged: () => EnsureCalendarDataIsCorrect());
        }
        public decimal SalesTotalExTax
        {
            get => _salesTotalExTax;
            set => SetProperty(ref _salesTotalExTax, value);
        }
        public decimal TotalTax
        {
            get => _totalTax;
            set => SetProperty(ref _totalTax, value);
        }
        public decimal SalesTotal
        {
            get => _salesTotal;
            set => SetProperty(ref _salesTotal, value);
        }
        public DateTime CalMinDate
        {
            get => _calMinDate;
            set => SetProperty(ref _calMinDate, value);
        }
        public DateTime CalMaxDate
        {
            get => _calMaxDate;
            set => SetProperty(ref _calMaxDate, value);
        }
        /// <summary>
        /// Is view Table view of Graph view
        /// </summary>
        /// <remarks>
        /// True = Table View; False = Graph View
        /// </remarks>
        public bool TableView
        {
            get => _tableView;
            set => SetProperty(ref _tableView, value);
        }
        public Layout<View> GridView
        {
            get => _gridView;
            set => SetProperty(ref _gridView, value);
        }
        #endregion

        public SalesReportsViewModel()
        {
            Title = "SalesReports".Translate();
            Icon = "";
            TableView = false;
            Series = new ChartSeriesCollection();
            Series.CollectionChanged += (sender, e) => OnPropertyChanged("Series");
            GridView = default;

            Device.BeginInvokeOnMainThread(() =>
            {
                Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
                using (var db = new Helpers.Database.Database(databaseProvider))
                {
                    db.SetTrackingBehavior(QueryTrackingBehavior.NoTracking);
                    CalMaxDate = db.Get<SaleModel>().OrderByDescending(s => s.DateOfSale).Select(s => s.DateOfSale).FirstOrDefault();
                    if (CalMaxDate != default)
                        CalMaxDate = CalMaxDate.StartOfWeek().AddDays(6);

                    CalMinDate = db.Get<SaleModel>().OrderBy(s => s.DateOfSale).Select(s => s.DateOfSale).FirstOrDefault();
                    if (CalMinDate != default)
                        CalMinDate = CalMinDate.StartOfWeek();

                    SelectionRange = new SelectionRange
                    {
                        StartDate = DateTime.Now.StartOfWeek(),
                        EndDate = DateTime.Now.StartOfWeek().AddDays(6)
                    };
                }
            });
            App.SetLoading(false);
        }



        #region Command
        Command _displayTableCommand;

        public Command DisplayTableCommand
        {
            get => _displayTableCommand ?? (_displayTableCommand = new Command<StackLayout>(ExecuteDisplayTable));
        }

        #endregion

        #region ExecuteCommand
        private async void ExecuteDisplayTable(StackLayout stackLayout)
        {
            await GenerateTable(stackLayout);
        }
        #endregion

        #region Operation
        private void SalesDataLoading(Helpers.Database.Database db)
        {
            SalesTotalExTax = 0m;
            SalesTotal = 0m;
            var salesData = new List<Tuple<string, List<SalesDataByPayMethod>>>();
            for (int days = 0;
                days <= Math.Abs((SelectionRange.StartDate - SelectionRange.EndDate).TotalDays);
                days++)
            {
                var date = SelectionRange.StartDate.AddDays(days);
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

                    var exVat = paySales.Sum(ps => ps.Sale.TotalExTax * ((ps.Amount - ps.Change) / ps.Sale.Total));

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

                    paySales.ForEach(ps => SalesTotalExTax += ps.Sale.TotalExTax);
                    paySales.ForEach(ps => SalesTotal += ps.Sale.Total);
                }

                TotalTax = (SalesTotal - SalesTotalExTax) < 0 ? 0 : (SalesTotal - SalesTotalExTax);
            }
            CreateSeries(salesData);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="salesData"></param>
        private void CreateSeries(List<Tuple<string, List<SalesDataByPayMethod>>> salesData)
        {
            Series.Clear();

            foreach(var salesDatum in salesData)
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
        }

        /// <summary>
        /// 
        /// </summary>
        private void EnsureCalendarDataIsCorrect()
        {
            Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
            using (var db = new Helpers.Database.Database(databaseProvider))
            {
                /*
                if (Math.Abs((_selectionRange.StartDate - _selectionRange.EndDate).TotalDays) % 6 != 0)
                {
                    var diff = (_selectionRange.StartDate - _selectionRange.EndDate).TotalDays % 6;
                    if (diff > 0)
                        _selectionRange.EndDate = _selectionRange.EndDate.AddDays(diff);
                    else
                        _selectionRange.StartDate = _selectionRange.StartDate.AddDays(diff);
                    OnPropertyChanged("SelectionRange");
                }*/
                SalesDataLoading(db);
            }
        }

        private async Task GenerateTable(StackLayout stack)
        {
            var salesDataByDate = new List<ExpandoObject>();

            for (int days = 0;
                days <= Math.Abs((SelectionRange.StartDate - SelectionRange.EndDate).TotalDays);
                days++)
            {
                var saleDataByDate = new ExpandoObject() as IDictionary<string, object>;
                saleDataByDate.Add("Day", SelectionRange.StartDate.AddDays(days).DayOfWeek.ToString());
                saleDataByDate.Add("Date",
                    $"{SelectionRange.StartDate.AddDays(days).Date.ToShortDateString()}" +
                    $"\n" +
                    $"{SelectionRange.StartDate.AddDays(days).DayOfWeek.ToString()}");
                saleDataByDate.Add("Tax", default(decimal));

                foreach (var columnSeries in Series.Cast<ColumnSeries>())
                {
                    foreach (var salesData in columnSeries.ItemsSource.Cast<SalesDataByPayMethod>())
                    {
                        if (salesData.Date.Equals(saleDataByDate["Date"]))
                            saleDataByDate.Add(salesData.PayMethod, salesData.Monies);
                    }
                }
                salesDataByDate.Add(saleDataByDate as ExpandoObject);
            }
            using(ExcelEngine excelEngine = new ExcelEngine())
            {
                IApplication application = excelEngine.Excel;

                application.DefaultVersion = ExcelVersion.Excel2013;

                IWorkbook workbook = application.Workbooks.Create(1);

                IWorksheet worksheet = workbook.Worksheets[0];

                foreach(var columns in salesDataByDate as IDictionary<string, object>)
                {

                }

                MemoryStream stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                await DependencyService.Get<IFile>().SaveAndView("Test-TableExcel-Export.xlsx", "application/msexcel", stream, new Dictionary<string, List<string>> { { "Excel", new List<string>() { ".xlsx" } } });
            }
        }
        #endregion
    }

    public class SalesDataByPayMethod
    {
        public string PayMethod { get; set; }
        public string Day { get; set; }
        public string Date { get; set; }
        public decimal Monies { get; set; }
    }
}
