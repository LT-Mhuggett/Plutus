using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Database.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Admin
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class AddPaymentMeth : ContentPage
    {
        public PaymentMethodModel PayMeth {get;set;}
		public AddPaymentMeth ()
		{
			InitializeComponent ();
            PayMeth = new PaymentMethodModel();
            this.BindingContext = PayMeth;
		}

        private async void Conf_Clicked(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(PayMeth.Name))
            {
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), "Please name the Payment Method", App.Translate.ProvideValue("OK"));
                return;
            }
            App.DbContext.Add(PayMeth);
            if(!App.DbContext.Save())
            {
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("DbIssue"), App.Translate.ProvideValue("OK"));
                return;
            }
            App.DbContext = new Helpers.Database(App.AppSettings.DatabaseProvider);
            PayMeth = new PaymentMethodModel();
            this.BindingContext = PayMeth;
        }
    }
}