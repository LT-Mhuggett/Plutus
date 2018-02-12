using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Helpers.Extensions;
using Plutus.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Reports
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class WeeklyStockOuttakesPage : ContentPage
	{
        public ObservableCollection<TransactionModel> Sales { get; set; }

		public WeeklyStockOuttakesPage ()
		{
			InitializeComponent ();

		    var dateStart = DateTime.Now.StartOfWeek(DayOfWeek.Monday);
		    var dateEnd = dateStart.AddDays(6);

		    var tempSale = App.DbContext.GetSales().Where(s => s.DateOfSale > dateStart && s.DateOfSale < dateEnd).ToList();
		    Sales = new ObservableCollection<TransactionModel>();
		    foreach (var temp in tempSale)
		    {
		        foreach (var tempTran in temp.Transactions)
		        {
		            Sales.Add(tempTran);
                }
		    }

		    Sales = new ObservableCollection<TransactionModel>(Sales.OrderBy(s => s.Amount).ToList());

            BindingContext = this;
		}
	}
}