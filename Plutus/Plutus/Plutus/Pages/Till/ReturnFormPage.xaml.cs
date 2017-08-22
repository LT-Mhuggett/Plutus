using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Till
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class ReturnFormPage : ContentPage
	{
        public Basket BItem { get; set; }
        public Basket OBItem { get; set; }
		public ReturnFormPage (Basket temp)
		{
			InitializeComponent ();
            BItem = new Basket(temp);
            OBItem = temp;
            ItemName.Text = BItem.Name;
		}

        private async void Confirm_Clicked(object sender, EventArgs e)
        {
            if (!String.IsNullOrWhiteSpace(SaleID.Text) && !String.IsNullOrWhiteSpace(Reason.Text))
            {
                //if (App.DbContext.CheckSaleID(SaleID.Text))
                //{
                    BItem.SaleId = SaleID.Text;
                    BItem.Reason = Reason.Text;
                    BItem.Price = Decimal.Negate(BItem.Price);
                    BItem.Return = true;
                    MainPage.ReturnListener(OBItem, BItem, MainPage.Instance);
                    await Navigation.PopModalAsync();
                //}
            }
        }
    }
}