using Database.Models;
using Microsoft.Maui.ApplicationModel;
using Microsoft.EntityFrameworkCore;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Services.Analytics;
using Syncfusion.Maui.Calendar;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.ViewModels.MainTill.Statistics
{
    public class StockOuttakeViewModel : BaseViewModel
    {
        #region Fields
        private ObservableCollection<ItemModel> _itemsSold;
        private bool _listEmpty;
        private CalendarDateRange _selectionRange;
        private DateTime _calMinDate;
        private DateTime _calMaxDate;
        #endregion

        #region Properties
        public ObservableCollection<ItemModel> ItemsSold
        {
            get => _itemsSold;
            set => SetProperty(ref _itemsSold, value);
        }
        public bool ListEmpty
        {
            get => _listEmpty;
            set => SetProperty(ref _listEmpty, value);
        }
        public CalendarDateRange CalendarDateRange
        {
            get => _selectionRange;
            set => SetProperty(ref _selectionRange, value, onChanged: () => EnsureCalendarDataIsCorrect());
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
        #endregion

        public StockOuttakeViewModel()
        {
            Title = "StockOuttakeReport".Translate();
            Icon = "";
            ItemsSold = new ObservableCollection<ItemModel>();
            App.SetLoading(false);
            ItemsSold.CollectionChanged += (sender, e) =>
            {
                OnPropertyChanged("ItemsSold");
            };

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
        }

        private void ItemDataLoading(Helpers.Database.Database db)
        {
            ItemsSold.Clear();
            var sales = db.Get<SaleModel>()
                                  .Where(s => s.DateOfSale >= CalendarDateRange.StartDate
                                            && s.DateOfSale <= CalendarDateRange.EndDate)
                                  .Include(s => s.Transactions)
                                    .ThenInclude(t => t.Item)
                                        .ThenInclude(i => i.Cat);
            foreach (var tempSale in sales)
            {
                foreach (var tempTran in tempSale.Transactions)
                {
                    var item = tempTran.Item;
                    item.Amount = tempTran.Amount;
                    var dealtWith = false;
                    foreach (var tempItem in ItemsSold)
                    {
                        if (!tempItem.Id.Equals(item.Id)) continue;
                        tempItem.Amount += item.Amount;
                        dealtWith = true;
                    }
                    if (!dealtWith)
                        ItemsSold.Add(item);
                }
            }
            if (ItemsSold.Count == 0)
                ListEmpty = true;
            else
                ListEmpty = false;
        }

        #region Operation
        private void EnsureCalendarDataIsCorrect()
        {
            Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
            using (var db = new Helpers.Database.Database(databaseProvider))
            {
                ItemDataLoading(db);
                Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Stock Outtake Report Generated", new Dictionary<string, string>
                {
                    { "Start Date", CalendarDateRange.StartDate.Value.ToString("MM-dd-yyyy") },
                    { "End Date", CalendarDateRange.EndDate.Value.ToString("MM-dd-yyyy") }
                });
            }
        }
        #endregion
    }
}
