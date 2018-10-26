using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Database.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Till
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class ReturnFormPage : ContentPage
	{
        public ItemModel BItem { get; set; }
        public ItemModel OBItem { get; set; }

        /// <summary>
        /// Basic constructor for ReturnFromPage
        /// initalises Bitem and OBItem
        /// </summary>
        /// <param name="temp">Item to return</param>
		public ReturnFormPage (ItemModel temp, ItemModel oldTemp)
		{
			InitializeComponent();
            BItem = temp;
            OBItem = oldTemp;
            ItemName.Text = BItem.Name;
		}

        /// <summary>
        /// Ensures all fields are set, and correct using DB values and checks
        /// then test to see if the item is viable for a return if not then throw error message detailing problem
        /// if all are true then run MainPAge.ReturnListener and pop current page
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private async void Confirm_Clicked(object sender, EventArgs e)
        {
            if (!String.IsNullOrWhiteSpace(SaleID.Text) && !String.IsNullOrWhiteSpace(Reason.Text))
            {
                if (App.DbContext.IsIdSame<SaleModel, string>(SaleID.Text))
                {
                    List<TransactionModel> transList = App.DbContext.CheckItemExistInSale(SaleID.Text, BItem.Id).ToList();
                    TransactionModel trans = null;
                    if (transList.Count > 1)
                    {
                        foreach (var transTemp in transList)
                        {
                            if (transTemp.CheckoutItemChange != null && transTemp.CheckoutItemChange.Price == OBItem.Price ||
                                transTemp.ItemCostPrice == OBItem.Price && transTemp.ItemCostExPrice == OBItem.ExPrice)
                            {
                                trans = transTemp;
                                break;
                            }
                        }
                    }
                    else
                        trans = transList.Last();

                    if (trans!=null)
                    {
                        if (trans.Amount >= OBItem.Amount)
                        {
                            int amount=0;
                            if (trans.Sale.Refunded != null)
                            {
                                amount = trans.Sale.Refunded.Where(r => r.ItemId.Equals(BItem.Id)).Sum(r => r.Amount);
                            }

                            if (amount > OBItem.Amount)
                            {
                                await DisplayAlert(App.Translate.ProvideValue("Hmm"), String.Format(App.Translate.ProvideValue("NoRefundsLeftMesg"), BItem.Name), App.Translate.ProvideValue("OK"));
                                return;
                            }
                            if (BItem.Price != OBItem.Price || BItem.Price != trans.ItemCostPrice)
                            {
                                BItem.OGPrices = new Tuple<decimal, decimal>(BItem.ExPrice, BItem.Price);
                                BItem.ExPrice = trans.CheckoutItemChange == null ? trans.ItemCostExPrice : trans.CheckoutItemChange.ExPrice;
                                BItem.Price = trans.CheckoutItemChange == null ? trans.ItemCostPrice : trans.CheckoutItemChange.Price;
                            }
                            BItem.SaleId = SaleID.Text;
                            BItem.Reason = Reason.Text;
                            BItem.Return = true;
                            MainPage.ReturnListener(OBItem, BItem, MainPage.Instance);
                            await Navigation.PopAsync();
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