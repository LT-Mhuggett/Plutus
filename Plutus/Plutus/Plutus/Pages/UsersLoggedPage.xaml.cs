using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Plutus.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using System.Collections.Generic;
using I18N_L10N;
using Plutus.Helpers;
using ZXing.Mobile;
using ZXing.Net.Mobile.Forms;

namespace Plutus.Pages
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class UsersLoggedPage : ContentPage
    {
        private ZXingScannerPage _scanPage;

        public ObservableCollection<EmployeeModel> Emps { get; set; }

        /// <summary>
        /// Basic constructor for UsersLoggedPage
        /// this initalises Emps collection and sets the binding context
        /// </summary>
        public UsersLoggedPage()
        {
            InitializeComponent();

            Title = App.Translate.ProvideValue("ActivUsers");

            Emps = new ObservableCollection<EmployeeModel>(App.EmpsLogged);

            BindingContext = this;
        }

        //This will show basic user information that any employee would have access to
        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Handle_ItemTapped(object sender, ItemTappedEventArgs e)
        {
            if (e.Item == null)
                return;
            //Deselect Item
            ((ListView)sender).SelectedItem = null;
        }

        /// <summary>
        /// This removes the user from EmpsLogged to ensure they are logged out
        /// if there are no more users logged in then App MainPage is set to LoginPage and a message is displayed to user
        /// </summary>
        /// <param name="sender">object that called method</param>
        /// <param name="e">Event called by object</param>
        private void OnDelete(object sender, EventArgs e)
        {
            var menuItem = (EmployeeModel)((MenuItem)sender).CommandParameter;

            VerifyId.IsVisible = true;
            MPage.IsEnabled = false;
            if (Device.Idiom == TargetIdiom.Desktop)
            {
                EId.Focus();
                Confirm.CommandParameter=menuItem;
            }
            else
            {
                var opt = new MobileBarcodeScanningOptions
                {
                    DelayBetweenContinuousScans = 3000,
                    UseNativeScanning = true,
                    TryHarder = true,
                    TryInverted = true
                };
                _scanPage = new ZXingScannerPage(opt, null);
                _scanPage.OnScanResult += (result) =>
                {
                    Device.BeginInvokeOnMainThread(() =>
                    {
                        Delete(menuItem, result.Text);
                    });
                };
            }
        }

        /// <summary>
        /// Shows LoginPage
        /// </summary>
        /// <param name="sender">object that called method</param>
        /// <param name="e">Event called by object</param>
        private void NewUserLogin_Clicked(object sender, EventArgs e)
        {
            //Application.Current.MainPage = new NavigationPage(new LoginPage(Emps));
            Navigation.PushAsync(new LoginPage());
        }

        /// <summary>
        /// Logs out all users this is only allowed to be used a authorised users
        /// </summary>
        /// <param name="sender">object that called method</param>
        /// <param name="e">Event called by object</param>
        private void LogoutAll_Clicked(object sender, EventArgs e)
        {
            Action action = async () => {
                App.EmpsLogged = new ObservableCollection<EmployeeModel>();
                await DisplayAlert(App.Translate.ProvideValue("Info"), App.Translate.ProvideValue("NoActiveUsers"), App.Translate.ProvideValue("OK"));
                App.Current.MainPage = new NavigationPage(new LoginPage());
            };

            Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Item", "A", action);
        }

        /// <summary>
        /// Runs the Delete method and gets command parameter from the confirm button
        /// </summary>
        /// <param name="sender">object that called method</param>
        /// <param name="e">Event called by object</param>
        private void Confirm_Clicked(object sender, EventArgs e)
        {
            Delete((EmployeeModel)Confirm.CommandParameter, EId.Text);
        }

        /// <summary>
        /// Removes the employee check popup
        /// </summary>
        /// <param name="sender">object that called method</param>
        /// <param name="e">Event called by object</param>
        private void Cancel_Clicked(object sender, EventArgs e)
        {
            EId.Text = null;
            Confirm.CommandParameter = null;
            VerifyId.IsVisible = false;
            MPage.IsEnabled = true;
        }

        /// <summary>
        /// If the authoristing employee is the employee to be removed the employee is removed else if the employee has authorisation to remove employees they are removed
        /// </summary>
        /// <param name="menuItem">Employee to be removed from List</param>
        /// <param name="result">Employee ID to test</param>
        private async void Delete(EmployeeModel menuItem, string result)
        {
            bool Delete = false;
            if (menuItem.Id == result)
            {
                Delete = true;
            }
            else
            {
                EmployeeModel Emp = null;
                foreach (var item in Emps)
                {
                    if (item.Id == result)
                    {
                        Emp = item;
                    }
                }
                if (Emp != null)
                    Delete = Authorisation.IsAuthorised("Force Loggout Single User", "X", Emp);
            }

            if (Delete)
            {
                Emps.Remove(menuItem);
                App.EmpsLogged.Remove(menuItem);

                if (Emps.Count == 0)
                {
                    await DisplayAlert(App.Translate.ProvideValue("Info"), App.Translate.ProvideValue("NoActiveUsers"), App.Translate.ProvideValue("OK"));
                    Application.Current.MainPage = new NavigationPage(new LoginPage());
                }
                EId.Text = null;
                Confirm.CommandParameter = null;
                VerifyId.IsVisible = false;
                MPage.IsEnabled = true;
            }
            else
            {
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
                return;
            }
        }

        private async void CancelMain_Clicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }
    }
}