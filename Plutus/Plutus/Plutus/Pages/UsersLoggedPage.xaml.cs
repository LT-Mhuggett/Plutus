using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Plutus.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using System.Collections.Generic;

namespace Plutus.Pages
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class UsersLoggedPage : ContentPage
    {
        public ObservableCollection<EmployeeModel> Emps { get; set; }

        public UsersLoggedPage(ObservableCollection<EmployeeModel> tempEmp)
        {
            InitializeComponent();

            Title = "Active Users";

            Emps = new ObservableCollection<EmployeeModel>(tempEmp);

            BindingContext = this;
        }

        public void Handle_ItemTapped(object sender, SelectedItemChangedEventArgs e)
        {
            if (e.SelectedItem == null)
                return;
            //Deselect Item
            ((ListView)sender).SelectedItem = null;
        }

        public async void OnDelete(object sender, EventArgs e)
        {
            var menuItem = (EmployeeModel)((MenuItem)sender).CommandParameter;

            Emps.Remove(menuItem);

            if (Emps.Count == 0)
            {
                MainNavigationPage.empsLogged = new ObservableCollection<EmployeeModel>();
                await DisplayAlert("Info", "No users are logged in", "OK");
                Application.Current.MainPage = new NavigationPage(new LoginPage());
            }
        }

        private void NewUserLogin_Clicked(object sender, EventArgs e)
        {
            Application.Current.MainPage = new NavigationPage(new LoginPage(Emps));
        }

        private void LogoutAll_Clicked(object sender, EventArgs e)
        {
            throw new NotImplementedException();
        }
    }
}