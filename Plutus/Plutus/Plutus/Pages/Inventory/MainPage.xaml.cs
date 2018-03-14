using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Helpers;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Inventory
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class MainPage : ContentPage
	{
        /// <summary>
        /// Basic constructor for MainPage[Inventory]
        /// </summary>
		public MainPage ()
		{
			InitializeComponent ();
		}

        /// <summary>
        /// Check user authorisation
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
        private void AddItem_Clicked(object sender, EventArgs e)
        {
            Action action = async () => await App.Current.MainPage.Navigation.PushAsync(new AddItemPage());
            Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Item", "A", action);
        }

        /// <summary>
        /// Check user authorisation
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
        private void UpdateItem_Clicked(object sender, EventArgs e)
        {
            Action action = async () => await App.Current.MainPage.Navigation.PushAsync(new UpdateItemPage());
            Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Item", "M", action);
        }

        /// <summary>
        /// Check user authorisation
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
        private void StockUpdate_Clicked(object sender, EventArgs e)
        {
            Action action = async () => await App.Current.MainPage.Navigation.PushAsync(new StockUpdatePage());
            Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Item", "M", action);
        }

        private void ViewAllI_Clicked(object sender, EventArgs e)
        {
            Action action = async () => await App.Current.MainPage.Navigation.PushAsync(new ViewAllInventoryPage());
            Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Item", "V", action);
        }

        private void CancelEmpCheck_Clicked(object sender, EventArgs e)
        {
            EId.Text = null;
            VerifyId.IsVisible = false;
            MPage.IsEnabled = true;
        }

        private void MassUpdate_Clicked(object sender, EventArgs e)
        {
            Action action = async () => await App.Current.MainPage.Navigation.PushAsync(new MassStockUpdateMainPage());
            Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Item", "V", action);
        }
    }
}