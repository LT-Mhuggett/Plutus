using Database.Models;
using Microsoft.Maui.ApplicationModel;
using Plutus.Frontend.AppClient.Helpers.Compatibility;
using Microsoft.EntityFrameworkCore;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Helpers.FileIO;
using Plutus.Frontend.AppClient.Services.Analytics;
using Plutus.Frontend.AppClient.Services.IOHandeling;
using Syncfusion.Maui.Calendar;
using Syncfusion.Maui.Charts;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.ViewModels.MainTill.Statistics
{
    public class SalesReportsViewModel : BaseViewModel
    {
        #region Private Fields
        private ChartSeriesCollection _series;
        private CalendarDateRange _selectionRange;
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
        private Layout _gridView;
        #endregion

        #region Properties
        public ChartSeriesCollection Series
        {
            get => _series;
            set => SetProperty(ref _series, value);
        }
        public CalendarDateRange CalendarDateRange
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
        public Layout GridView
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

            MainThread.BeginInvokeOnMainThread(() =>
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

                    CalendarDateRange = new CalendarDateRange(
                        DateTime.Now.StartOfWeek(),
                        DateTime.Now.StartOfWeek().AddDays(6));
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

        Command _exportSelectedDataCommand;

        public Command ExportSelectedDataCommand
        {
            get => _exportSelectedDataCommand ?? (_exportSelectedDataCommand = new Command(ExecuteExportData));
        }

        #endregion

        #region ExecuteCommand
        /// <summary>
        /// 
        /// </summary>
        /// <param name="stackLayout"></param>
        private async void ExecuteDisplayTable(StackLayout stackLayout)
        {
            await GenerateTable(stackLayout);
        }

        /// <summary>
        /// Start the execution process of exporting the data in the selected range
        /// </summary>
        public async void ExecuteExportData()
        {
            //var FileData = new List<(string fileName, string contentType, MemoryStream stream)>();

            Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
            using (var db = new Helpers.Database.Database(databaseProvider))
            {
                db.SetTrackingBehavior(QueryTrackingBehavior.NoTracking);
                await LoadRequiredExportDataAsync(CalendarDateRange.StartDate.Value, CalendarDateRange.EndDate.Value, db);
                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Export Sales Report", new Dictionary<string, string>
                {
                    { "Start Date", CalendarDateRange.StartDate.Value.ToString("MM-dd-yyyy") },
                    { "End Date", CalendarDateRange.EndDate.Value.ToString("MM-dd-yyyy") }
                });
            }

            //AppServices.Get<IFile>().SaveFiles();
        }
        #endregion

        #region Operation
        /// <summary>
        /// Loads in the selected data
        /// </summary>
        /// <param name="db"></param>
        private void SalesDataLoading(DateTime startDate, DateTime endDate, Helpers.Database.Database db)
        {
            SalesTotalExTax = 0m;
            SalesTotal = 0m;
            var salesData = new List<Tuple<string, List<SalesDataByPayMethod>>>();
            for (int days = 0;
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
            CreateSeries(salesData);
            Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Sales Report Generated", new Dictionary<string, string>
                {
                    { "Start Date", startDate.ToString("MM-dd-yyyy") },
                    { "End Date", endDate.ToString("MM-dd-yyyy") }
                });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="salesData"></param>
        private void CreateSeries(List<Tuple<string, List<SalesDataByPayMethod>>> salesData)
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
                    ShowDataLabels = true,
                    EnableTooltip = true,
                    TooltipTemplate = new DataTemplate(() =>
                    {
                        var stack = new StackLayout { Orientation = StackOrientation.Vertical };
                        var firstInner = new StackLayout { Orientation = StackOrientation.Horizontal };
                        var labelXTitle = new Label { Text = "Day:", TextColor = Colors.White };
                        var labelX = new Label { TextColor = Colors.White };
                        labelX.SetBinding(Label.TextProperty, "Day");
                        firstInner.Children.Add(labelXTitle);
                        firstInner.Children.Add(labelX);

                        var secondInner = new StackLayout { Orientation = StackOrientation.Horizontal };
                        var labelYTitle = new Label { Text = "Monies:", TextColor = Colors.White };
                        var labelY = new Label { TextColor = Colors.White };
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
                db.SetTrackingBehavior(QueryTrackingBehavior.NoTracking);
                /*
                if (Math.Abs((_selectionRange.StartDate - _selectionRange.EndDate).TotalDays) % 6 != 0)
                {
                    var diff = (_selectionRange.StartDate - _selectionRange.EndDate).TotalDays % 6;
                    if (diff > 0)
                        _selectionRange.EndDate = _selectionRange.EndDate.AddDays(diff);
                    else
                        _selectionRange.StartDate = _selectionRange.StartDate.AddDays(diff);
                    OnPropertyChanged("CalendarDateRange");
                }*/
                SalesDataLoading(CalendarDateRange.StartDate.Value, CalendarDateRange.EndDate.Value, db);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stack"></param>
        /// <returns></returns>
        private async Task GenerateTable(StackLayout stack)
        {
            /*
            var salesDataByDate = new List<ExpandoObject>();

            for (int days = 0;
                days <= Math.Abs((CalendarDateRange.StartDate - CalendarDateRange.EndDate).TotalDays);
                days++)
            {
                var saleDataByDate = new ExpandoObject() as IDictionary<string, object>;
                saleDataByDate.Add("Day", CalendarDateRange.StartDate.AddDays(days).DayOfWeek.ToString());
                saleDataByDate.Add("Date",
                    $"{CalendarDateRange.StartDate.AddDays(days).Date.ToShortDateString()}" +
                    $"\n" +
                    $"{CalendarDateRange.StartDate.AddDays(days).DayOfWeek.ToString()}");
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

                await AppServices.Get<IFile>().SaveAndView("Test-TableExcel-Export.xlsx", "application/msexcel", stream, new Dictionary<string, List<string>> { { "Excel", new List<string>() { ".xlsx" } } });
            }*/
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="startDate"></param>
        /// <param name="endDate"></param>
        /// <param name="db"></param>
        private async Task LoadRequiredExportDataAsync(DateTime startDate, DateTime endDate, Helpers.Database.Database db)
        {
            var dataSet = new DataSet();

            var data = db.GetAllBetweenDates(startDate, endDate)
                .Include(s => s.Transactions)
                    .ThenInclude(t => t.Item)
                .Include(s => s.Transactions)
                    .ThenInclude(t => t.CheckoutItemChange)
                .Include(s => s.Transactions)
                    .ThenInclude(t => t.Transaction_Discounts)
                        .ThenInclude(td => td.Discount)
                .Include(s => s.PaySales)
                    .ThenInclude(ps => ps.PayMethod)
                .Include(s => s.Refunds)
                    .ThenInclude(r => r.Item)
                .Include(s => s.Refunds)
                    .ThenInclude(r => r.Authoriser)
                .Include(s => s.Refunds)
                    .ThenInclude(r => r.CheckoutItemChange)
                .Include(s => s.Employee).ToList();

            var dailySalesSummaries = new List<IDictionary<string, object>>();

            var salesBreakdowns = new List<SalesBreakdown>();

            var currentDate = startDate;
            do
            {
                var dailySalesSummary = new Dictionary<string, object>
                {
                    { "Date".Translate(), currentDate.ToShortDateString() }
                };

                foreach (var payMethod in db.Get<PaymentMethodModel>())
                {
                    dailySalesSummary.Add(
                        payMethod.Name + $" ({"ExTax".Translate()})",
                            data.Where(s => s.DateOfSale.Date.Equals(currentDate.Date) && s.Total != decimal.Zero && s.TotalExTax != decimal.Zero)
                                .Sum(s => s.TotalExTax * (s.PaySales.Where(ps => ps.PayMethod.Id.Equals(payMethod.Id)).Sum(ps => ps.Amount - ps.Change) / s.Total)).Normalize());
                }

                dailySalesSummary.Add(
                    "Daily (ex Tax)",
                        data.Where(s => s.DateOfSale.Date.Equals(currentDate.Date)).Sum(s => s.TotalExTax));
                dailySalesSummary.Add(
                    "Daily (inc Tax)",
                        data.Where(s => s.DateOfSale.Date.Equals(currentDate.Date)).Sum(s => s.Total));

                dailySalesSummaries.Add(dailySalesSummary);

                currentDate = currentDate.AddDays(1);
            } while (currentDate <= endDate);

            currentDate = startDate;
            do
            {
                foreach (var salesData in data.Where(s => s.DateOfSale.Date.Equals(currentDate.Date)))
                {
                    foreach (var trans in salesData.Transactions)
                    {
                        //Record Transaction
                        salesBreakdowns.Add(new SalesBreakdown()
                        {
                            RecordDate = currentDate.ToShortDateString(),
                            SaleId = salesData.Id,
                            ItemId = trans.Item.Id,
                            ItemName = trans.Item.Name,
                            UnitPriceAtCheckout = trans.CheckoutItemChangeId == null ? trans.ItemCostPrice : trans.CheckoutItemChange.Price,
                            UnitPriceAtCheckoutExTax = trans.CheckoutItemChangeId == null ? trans.ItemCostExPrice : trans.CheckoutItemChange.ExPrice,
                            Qty = trans.Amount,
                            TotalSalePrice = (trans.CheckoutItemChangeId == null ? trans.ItemCostPrice : trans.CheckoutItemChange.Price) * trans.Amount,
                            TotalSalePriceExTax = (trans.CheckoutItemChangeId == null ? trans.ItemCostExPrice : trans.CheckoutItemChange.ExPrice) * trans.Amount,
                            EmployeeName = salesData.Employee.FullName
                        });

                        foreach (var transDiscount in trans.Transaction_Discounts)
                        {
                            var discountPrice = -decimal.Round(Math.Abs(transDiscount.Discount.Type == 0 ?
                                transDiscount.DiscountRate :
                                (trans.CheckoutItemChangeId == null ?
                                    trans.ItemCostPrice :
                                    trans.CheckoutItemChange.Price)
                                * transDiscount.DiscountRate), 2, MidpointRounding.AwayFromZero);
                            var discountExPrice = -decimal.Round(Math.Abs(transDiscount.Discount.Type == 0 ?
                                transDiscount.DiscountRate :
                                (trans.CheckoutItemChangeId == null ?
                                    trans.ItemCostExPrice :
                                    trans.CheckoutItemChange.ExPrice)
                                * transDiscount.DiscountRate), 2, MidpointRounding.AwayFromZero);
                            //Record Discount
                            salesBreakdowns.Add(new SalesBreakdown()
                            {
                                RecordDate = currentDate.ToShortDateString(),
                                SaleId = salesData.Id,
                                ItemId = $"{"Discount".Translate()}",
                                ItemName = $"{transDiscount.Discount.Name}, {trans.Item.Id}",
                                UnitPriceAtCheckout = discountPrice,
                                UnitPriceAtCheckoutExTax = discountExPrice,
                                Qty = trans.Amount,
                                TotalSalePrice = discountPrice * trans.Amount,
                                TotalSalePriceExTax = discountExPrice * trans.Amount
                            });
                        }
                    }

                    foreach (var refund in salesData.Refunds)
                        salesBreakdowns.Add(new SalesBreakdown()
                        {
                            RecordDate = currentDate.ToShortDateString(),
                            SaleId = salesData.Id,
                            ItemId = refund.Item.Id,
                            ItemName = refund.Item.Name,
                            UnitPriceAtCheckout = -Math.Abs(refund.CheckoutItemChangeId == null ? refund.Item.Price : refund.CheckoutItemChange.Price),
                            UnitPriceAtCheckoutExTax = -Math.Abs(refund.CheckoutItemChangeId == null ? refund.Item.ExPrice : refund.CheckoutItemChange.ExPrice),
                            Qty = refund.Amount,
                            TotalSalePrice = -Math.Abs((refund.CheckoutItemChangeId == null ? refund.Item.Price : refund.CheckoutItemChange.Price)) * refund.Amount,
                            TotalSalePriceExTax = -Math.Abs((refund.CheckoutItemChangeId == null ? refund.Item.ExPrice : refund.CheckoutItemChange.ExPrice)) * refund.Amount,
                            EmployeeName = refund.AuthoriserId == null ? salesData.Employee.FullName : refund.Authoriser.FullName
                        });
                }

                currentDate = currentDate.AddDays(1);
            } while (currentDate <= endDate);

            dataSet.Tables.Add(dailySalesSummaries.ToDataTable("DailySales".Translate()));
            dataSet.Tables.Add(salesBreakdowns.ToDataTable("SalesBreakdown".Translate()));

            using (var docHandler = new ExcelHandling())
            {
                docHandler.DataTableToWorksheet(dataSet);
                var fileStream = docHandler.Finalize();
                await AppServices.Get<IFile>().SaveAndView(
                        $"{"SalesReports".Translate()} - {startDate.ToShortDateString()}-{endDate.ToShortDateString()}",
                        "application/vnd.ms-excel",
                        fileStream,
                        new Dictionary<string, IList<string>>() { { "Excel", new List<string>() { ".xlsx", ".xls" } } }
                        );
            }
            /*
            var tablesInCSVFormat = new List<(string, string, MemoryStream, string)>();

            do
            {
                var table = tablesToLoad.Dequeue();

                var tableInCSVFormat = "";

                var orderOfProperty = new List<string>();

                foreach (var property in table.GetProperties())
                {
                    if (property.CustomAttributes.Any(cA => cA.AttributeType.IsEquivalentTo(typeof(Database.Attributes.Exportable))))
                    {
                        tableInCSVFormat += property.Name + ",";
                        orderOfProperty.Add(property.Name);
                    }
                }

                tableInCSVFormat = tableInCSVFormat.Remove(tableInCSVFormat.Length - 1);

                dynamic data;

                if (table == typeof(PaymentMethodModel))
                {
                    data = typeof(Helpers.Database.Database)
                        .GetMethod("Get", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .MakeGenericMethod(table)
                        .Invoke(db, null);
                }
                else
                {
                    data = typeof(Helpers.Database.Database)
                        .GetMethod("GetAllBetweenDates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .MakeGenericMethod(table)
                        .Invoke(db, new object[] { startDate, endDate });
                }

                foreach (var property in data)
                {
                    tableInCSVFormat += "\n";
                    foreach (var stringProptery in orderOfProperty)
                    {
                        tableInCSVFormat += property.GetType().GetProperty(stringProptery).GetValue(property, null) + ",";
                    }

                    tableInCSVFormat = tableInCSVFormat.Remove(tableInCSVFormat.Length - 1);
                }
                tablesInCSVFormat.Add((table.Name+".csv", "text/plain", tableInCSVFormat.ToStream(), "*"));

            } while (tablesToLoad.Count() > 0);

            var filesAndStatus = await AppServices.Get<IFile>().SaveFiles(tablesInCSVFormat);
            */
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

    public class SalesBreakdown
    {
        public string RecordDate { get; set; }
        public string SaleId { get; set; }
        public string ItemId { get; set; }
        public string ItemName { get; set; }
        public decimal UnitPriceAtCheckout { get; set; }
        public decimal UnitPriceAtCheckoutExTax { get; set; }
        public int Qty { get; set; }
        public decimal TotalSalePrice { get; set; }
        public decimal TotalSalePriceExTax { get; set; }
        public string EmployeeName { get; set; }
    }
}
