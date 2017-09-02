using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Helpers;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using ZXing.Mobile;
using ZXing.Net.Mobile.Forms;
using Plutus.Models;

namespace Plutus.Pages.Staff
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class MainPage : ContentPage
    {
        private ZXingScannerPage _scanPage;

        /// <summary>
        /// Basic constructor for MainPage[Staff]
        /// </summary>
		public MainPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Check user authorisation
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private void AddEmp_Clicked(object sender, EventArgs e)
        {
            Action action = async () => await App.Current.MainPage.Navigation.PushAsync(new AddEmployeePage());
            Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Staff", "A", action);
        }

        /// <summary>
        /// Check user authorisation
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private void DeactivateEmp_Clicked(object sender, EventArgs e)
        {
            Action action = async () => await App.Current.MainPage.Navigation.PushAsync(new EmployeeDeactivationPage());
            Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Staff", "R", action);
        }

        /// <summary>
        /// Check user authorisation
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private void ReadAllEmployee_Clicked(object sender, EventArgs e)
        {
            Action action = async () => await App.Current.MainPage.Navigation.PushAsync(new AllEmployees());
            Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Staff", "M", action);
        }

        private void CancelEmpCheck_Clicked(object sender, EventArgs e)
        {
            EId.Text = null;
            VerifyId.IsVisible = false;
            MPage.IsEnabled = true;
        }
    }
}