using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Helpers;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Database.Models;
using System.Collections.ObjectModel;
using Plutus.Helpers.Extensions;

namespace Plutus.Pages.Reports
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class MainPage : ContentPage
	{
        //public ObservableCollection<SaleModel> SalesData { get; set; }

		public MainPage ()
		{
			InitializeComponent();
		}

        private void SalesReps_Clicked(object sender, EventArgs e)
        {
            Action action = async () => await App.Current.MainPage.Navigation.PushAsync(new SalesReportsPage());
            Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Report", "V", action);
        }

        private void CancelEmpCheck_Clicked(object sender, EventArgs e)
        {
            EId.Text = null;
            VerifyId.IsVisible = false;
            MPage.IsEnabled = true;
        }

        private void WSOutReps_Clicked(object sender, EventArgs e)
        {
            Action action = async () => await App.Current.MainPage.Navigation.PushAsync(new WeeklyStockOuttakesPage());
            Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Report", "V", action);
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            //SalesData = App.DbContext.Get<SaleModel>().ToModel<ObservableCollection<SaleModel>>();
        }
    }
}