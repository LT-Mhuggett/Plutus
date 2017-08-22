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
                if (App.DbContext.CheckSaleID(SaleID.Text))
                {
                    TransactionModel trans = App.DbContext.CheckItemExistInSale(SaleID.Text, BItem.ItemId);
                    if (trans!=null)
                    {
                        if (trans.Amount >= BItem.Amount)
                        {
                            int amount=0;
                            if (trans.Sale.Refunded != null)
                            {
                                amount = trans.Sale.Refunded.Where(r => r.ItemId.Equals(BItem.ItemId)).Sum(r => r.Amount);
                            }
                            if (amount < BItem.Amount)
                            {

                                BItem.SaleId = SaleID.Text;
                                BItem.Reason = Reason.Text;
                                BItem.Price = Decimal.Negate(BItem.Price);
                                BItem.Return = true;
                                MainPage.ReturnListener(OBItem, BItem, MainPage.Instance);
                                await Navigation.PopAsync();
                            }
                            else
                            {
                                await DisplayAlert(App.Translate.ProvideValue("Hmm"), String.Format(App.Translate.ProvideValue("NoRefundsLeftMesg"), BItem.Name), App.Translate.ProvideValue("OK"));
                                return;
                            }
                        }
                        else
                        {
                            await DisplayAlert(App.Translate.ProvideValue("Hmm"), String.Format(App.Translate.ProvideValue("ItemAmountExceedsMesg"), BItem.Name, trans.Amount, BItem.Amount - trans.Amount), App.Translate.ProvideValue("OK"));
                            return;
                        }
                    }
                    else
                    {
                        await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("ItemNotExistInSaleMesg"), App.Translate.ProvideValue("OK"));
                        return;
                    }
                }
                else
                {
                    await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("SaleIDWrongMesg"), App.Translate.ProvideValue("OK"));
                    return;
                }
            }
            else
            {
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("FieldsFilledInMesg"), App.Translate.ProvideValue("OK"));
                return;
            }
        }
    }
}