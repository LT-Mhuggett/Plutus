using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Helpers.Extensions;
using Plutus.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Reports
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class WeeklyStockOuttakesPage : ContentPage
	{
        public ObservableCollection<ItemModel> Sales { get; set; }

		public WeeklyStockOuttakesPage ()
		{
			InitializeComponent ();

		    var dateStart = DateTime.Now.StartOfWeek(DayOfWeek.Monday);
		    var dateEnd = dateStart.AddDays(7);

		    var tempSale = App.DbContext.GetSales().Where(s => s.DateOfSale > dateStart && s.DateOfSale < dateEnd).AsNoTracking().ToList();
		    Sales = new ObservableCollection<ItemModel>();
		    foreach (var temp in tempSale)
		    {
		        foreach (var tempTran in temp.Transactions)
		        {
		            var item = tempTran.Item;
		            item.Amount = tempTran.Amount;
                    var dealtWith = false;
		            foreach (var tempItem in Sales)
		            {
		                if (!tempItem.Id.Equals(item.Id)) continue;
		                tempItem.Amount += item.Amount;
		                dealtWith = true;
		            }
		            if (!dealtWith)
		                Sales.Add(item);
		        }
		    }

		    Sales = new ObservableCollection<ItemModel>(Sales.OrderByDescending(i => i.Amount).ToList());

            BindingContext = this;
		}
	}
}